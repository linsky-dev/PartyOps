"""新增账号恢复凭据，保留既有用户、会话与审计数据。"""

import sqlalchemy as sa
from alembic import op

revision = "0027_account_recovery"
down_revision = "0026"
branch_labels = None
depends_on = None


def upgrade() -> None:
    if sa.inspect(op.get_bind()).has_table("recovery_credentials"):
        return  # 0001 从当前 ORM 元数据创建的全新库已包含此表。
    op.create_table(
        "recovery_credentials",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column(
            "user_id", sa.String(length=36),
            sa.ForeignKey("users.id", ondelete="CASCADE"), nullable=False,
        ),
        sa.Column("purpose", sa.String(length=20), nullable=False),
        sa.Column("digest", sa.String(length=64), nullable=False, unique=True),
        sa.Column("expires_at", sa.DateTime(timezone=True)),
        sa.Column("used_at", sa.DateTime(timezone=True)),
        sa.Column("revoked_at", sa.DateTime(timezone=True)),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_index("ix_recovery_credentials_user_id", "recovery_credentials", ["user_id"])


def downgrade() -> None:
    # 未使用该能力的合成旧库可用来演练迁移，不删除已登记的恢复凭据。
    if op.get_bind().scalar(sa.text("SELECT 1 FROM recovery_credentials LIMIT 1")):
        raise RuntimeError("账号恢复迁移已有凭据，请保留认证状态并前向修复。")
    op.drop_table("recovery_credentials")
