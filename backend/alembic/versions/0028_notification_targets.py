"""保存通知的子对象定位信息，旧通知保留原对象字段。"""

import sqlalchemy as sa
from alembic import op

revision = "0028"
down_revision = "0027"
branch_labels = None
depends_on = None


def upgrade() -> None:
    columns = sa.inspect(op.get_bind()).get_columns("notifications")
    if not any(column["name"] == "target" for column in columns):
        op.add_column("notifications", sa.Column("target", sa.JSON(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("notifications") as batch:
        batch.drop_column("target")
