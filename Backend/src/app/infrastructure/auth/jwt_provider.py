import hashlib
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

from jose import jwt  # type: ignore[import-untyped]

from app.config import Settings


class JwtProvider:
    _ISSUER = "starfunc-api"
    _AUDIENCE = "starfunc-client"

    def __init__(self, settings: Settings) -> None:
        self._secret = settings.jwt_secret
        self._algorithm = settings.jwt_algorithm
        self._access_ttl = timedelta(minutes=settings.jwt_access_token_expire_minutes)
        self._refresh_ttl = timedelta(days=settings.jwt_refresh_token_expire_days)

    def create_access_token(self, player_id: UUID, platform: str) -> str:
        now = datetime.now(UTC)
        payload = {
            "sub": str(player_id),
            "platform": platform,
            "iat": now,
            "exp": now + self._access_ttl,
            "iss": self._ISSUER,
            "aud": self._AUDIENCE,
        }
        return str(jwt.encode(payload, self._secret, algorithm=self._algorithm))

    def create_refresh_token(self, player_id: UUID) -> str:
        now = datetime.now(UTC)
        payload = {
            "sub": str(player_id),
            "iat": now,
            "exp": now + self._refresh_ttl,
            "type": "refresh",
            "jti": str(uuid4()),
        }
        return str(jwt.encode(payload, self._secret, algorithm=self._algorithm))

    def decode(self, token: str) -> dict[str, Any]:
        return dict(
            jwt.decode(
                token,
                self._secret,
                algorithms=[self._algorithm],
                audience=self._AUDIENCE,
                issuer=self._ISSUER,
            )
        )

    def decode_refresh(self, token: str) -> dict[str, Any]:
        """Decode a refresh token (no audience/issuer validation)."""
        return dict(
            jwt.decode(
                token,
                self._secret,
                algorithms=[self._algorithm],
            )
        )

    @staticmethod
    def hash_token(token: str) -> str:
        return hashlib.sha256(token.encode()).hexdigest()
