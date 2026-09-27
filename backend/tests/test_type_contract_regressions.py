"""类型收窄不能改变时间、XML 文本与启动参数的运行语义。"""

from datetime import date, datetime, timedelta, timezone
from inspect import signature
from typing import get_type_hints

import pytest
from lxml import etree
from uvicorn import Config

from app import main, official_format, reports
from app.routers import party_development, router_utils


@pytest.mark.parametrize(
    "normalize", [router_utils.aware_utc, party_development._aware, reports._aware]
)
@pytest.mark.parametrize("value", [None, datetime(2026, 9, 2, 0, 0)])
def test_nullable_time_contract_keeps_existing_utc_semantics(normalize, value):
    result = normalize(value)
    if value is None:
        assert result is None
    else:
        assert result == value.replace(tzinfo=timezone.utc)


@pytest.mark.parametrize(
    "normalize", [router_utils.aware_utc, party_development._aware, reports._aware]
)
def test_aware_time_contract_preserves_original_instant_and_offset(normalize):
    value = datetime(2026, 9, 2, 8, 0, tzinfo=timezone(timedelta(hours=8)))
    assert normalize(value) is value


def test_date_conversion_keeps_null_and_midnight_contract():
    assert party_development._as_datetime(None) is None
    assert party_development._as_datetime(date(2026, 9, 2)) == datetime(
        2026, 9, 2, tzinfo=timezone.utc
    )


def test_startup_options_match_real_uvicorn_keyword_contract():
    fields = get_type_hints(main._UvicornOptions)
    assert set(fields) <= set(signature(Config).parameters)
    assert {"host", "port", "ssl_cert_reqs", "lifespan"} <= set(fields)


def test_xpath_type_narrowing_preserves_chinese_runs_and_spaces():
    paragraph = etree.fromstring(
        f'<w:p xmlns:w="{official_format.W}"><w:r><w:t> 首行</w:t></w:r>'
        '<w:r><w:t>  二字缩进，内容不改。</w:t></w:r></w:p>'.encode()
    )
    before = etree.tostring(paragraph)
    assert official_format._paragraph_text(paragraph) == "首行  二字缩进，内容不改。"
    assert etree.tostring(paragraph) == before
