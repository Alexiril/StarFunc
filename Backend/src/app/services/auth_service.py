"""AuthService — registration, token refresh, theft detection, account linking."""

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID

from fastapi import HTTPException, status
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.schemas.auth import AuthResponse, LinkAccountRequest, LinkResponse, RefreshRequest, RegisterRequest
from app.config import Settings
from app.domain.exceptions import ConflictError, NotFoundError
from app.infrastructure.auth.apple_verifier import AppleGameCenterVerifier
from app.infrastructure.auth.google_verifier import GooglePlayVerifier
from app.infrastructure.auth.jwt_provider import JwtProvider
from app.infrastructure.persistence.models import PlayerSaveModel, RefreshTokenModel
from app.infrastructure.persistence.player_repo import PlayerRepository

# Default save data for newly registered players
_DEFAULT_SAVE_DATA: dict[str, Any] = {
    "saveVersion": 1,
    "version": 1,
    "lastModified": 0,
    "currentSectorIndex": 0,
    "sectorProgress": {},
    "levelProgress": {},
    "totalFragments": 0,
    "currentLives": 5,
    "lastLifeRestoreTimestamp": 0,
    "ownedItems": [],
    "consumables": {},
    "totalLevelsCompleted": 0,
    "totalStarsCollected": 0,
    "totalPlayTime": 0.0,
}


class AuthService:
    def __init__(self, jwt_provider: JwtProvider, settings: Settings) -> None:
        self._jwt = jwt_provider
        self._settings = settings

    async def register(self, request: RegisterRequest, session: AsyncSession) -> AuthResponse:
        repo = PlayerRepository(session)
        player = await repo.find_by_device_id(request.device_id)
        is_new = player is None

        if is_new:
            player = await repo.create(
                device_id=request.device_id,
                platform=request.platform.value,
                client_version=request.client_version,
            )
            # Create initial save
            save = PlayerSaveModel(
                player_id=player.id,
                save_data=_DEFAULT_SAVE_DATA.copy(),
            )
            session.add(save)

        assert player is not None
        access_token = self._jwt.create_access_token(player.id, request.platform.value)
        refresh_token = self._jwt.create_refresh_token(player.id)

        await self._store_refresh_token(session, player.id, refresh_token)
        await session.commit()

        return AuthResponse(
            player_id=player.id,
            access_token=access_token,
            refresh_token=refresh_token,
            expires_in=self._settings.jwt_access_token_expire_minutes * 60,
            is_new_player=is_new,
        )

    async def refresh(self, request: RefreshRequest, session: AsyncSession) -> AuthResponse:
        token_hash = self._jwt.hash_token(request.refresh_token)

        # Find refresh token record by hash
        stmt = select(RefreshTokenModel).where(RefreshTokenModel.token_hash == token_hash)
        result = await session.execute(stmt)
        token_record = result.scalar_one_or_none()

        if token_record is None:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail={"code": "INVALID_TOKEN", "message": "Refresh token not found"},
            )

        # Theft detection: if token is already revoked, revoke the entire chain
        if token_record.is_revoked:
            await self._revoke_all_tokens(session, token_record.player_id)
            await session.commit()
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail={"code": "INVALID_TOKEN", "message": "Token reuse detected, all tokens revoked"},
            )

        # Check expiration
        if token_record.expires_at < datetime.now(UTC):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail={"code": "TOKEN_EXPIRED", "message": "Refresh token has expired"},
            )

        # Revoke current token
        token_record.is_revoked = True

        # Decode to get player_id and platform
        player_id = UUID(self._jwt.decode_refresh(request.refresh_token)["sub"])

        # Look up platform from DB
        repo = PlayerRepository(session)
        player = await repo.find_by_id(player_id)
        if player is None:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail={"code": "INVALID_TOKEN", "message": "Player not found"},
            )

        # Generate new token pair
        access_token = self._jwt.create_access_token(player.id, player.platform)
        new_refresh_token = self._jwt.create_refresh_token(player.id)

        new_record = await self._store_refresh_token(session, player.id, new_refresh_token)

        # Link old → new
        token_record.replaced_by_id = new_record.id

        await session.commit()

        return AuthResponse(
            player_id=player.id,
            access_token=access_token,
            refresh_token=new_refresh_token,
            expires_in=self._settings.jwt_access_token_expire_minutes * 60,
            is_new_player=False,
        )

    async def link_account(
        self,
        player_id: UUID,
        request: LinkAccountRequest,
        session: AsyncSession,
    ) -> LinkResponse:
        repo = PlayerRepository(session)

        # Verify token with the appropriate provider
        if request.provider == "google_play":
            verifier: GooglePlayVerifier | AppleGameCenterVerifier = GooglePlayVerifier()
            provider_id = await verifier.verify(request.provider_token)
            existing = await repo.find_by_google_play_id(provider_id)
        else:
            verifier = AppleGameCenterVerifier()
            provider_id = await verifier.verify(request.provider_token)
            existing = await repo.find_by_apple_gc_id(provider_id)

        # Check uniqueness: provider_id already linked to a different player
        if existing is not None and existing.id != player_id:
            raise ConflictError(
                code="ACCOUNT_ALREADY_LINKED",
                message=f"This {request.provider} account is already linked to another player",
            )

        # Find current player
        player = await repo.find_by_id(player_id)
        if player is None:
            raise NotFoundError(message="Player not found")

        # Update the provider ID on the player record
        if request.provider == "google_play":
            player.google_play_id = provider_id
        else:
            player.apple_gc_id = provider_id

        await session.commit()

        return LinkResponse(provider=request.provider, provider_id=provider_id, linked=True)

    async def _store_refresh_token(
        self,
        session: AsyncSession,
        player_id: UUID,
        raw_token: str,
    ) -> RefreshTokenModel:
        token_hash = self._jwt.hash_token(raw_token)
        expires_at = datetime.now(UTC) + timedelta(days=self._settings.jwt_refresh_token_expire_days)

        record = RefreshTokenModel(
            player_id=player_id,
            token_hash=token_hash,
            expires_at=expires_at,
        )
        session.add(record)
        await session.flush()
        return record

    @staticmethod
    async def _revoke_all_tokens(session: AsyncSession, player_id: UUID) -> None:
        stmt = (
            update(RefreshTokenModel)
            .where(RefreshTokenModel.player_id == player_id, RefreshTokenModel.is_revoked.is_(False))
            .values(is_revoked=True)
        )
        await session.execute(stmt)
