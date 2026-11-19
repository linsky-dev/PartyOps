"""保存通知的子对象定位信息，旧通知保留原对象字段。"""

import sqlalchemy as sa
from alembic import op

revision = "0028_notification_targets"
down_revision = "0027_account_recovery"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("notifications", sa.Column("target", sa.JSON(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("notifications") as batch:
        batch.drop_column("target")
