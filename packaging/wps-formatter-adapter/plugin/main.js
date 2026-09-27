/*
 * PartyOps WPS 本机桥接探针。
 *
 * 这里不实现排版规则，只证明目标 WPS 能以字符为单位设置首行缩进、保存
 * DOCX 并保持后台运行。正式六功能适配必须继续映射原 C# 源码规则，并由
 * runtime-evidence.json 的真实金样门禁证明。
 */
(function (global) {
  "use strict";

  function requireString(value, name) {
    if (typeof value !== "string" || value.length === 0 || value.length > 4096) {
      throw new Error("参数无效：" + name);
    }
    return value;
  }

  function requireToken(value) {
    var config = global.PARTYOPS_WPS_BRIDGE_CONFIG;
    var expected = config && config.token;
    if (typeof expected !== "string" || expected.length < 32 || value !== expected) {
      throw new Error("PARTYOPS_WPS_BRIDGE_TOKEN_INVALID");
    }
    return config;
  }

  function closeDocument(document, saveChanges) {
    if (!document) return;
    try {
      document.Close(saveChanges ? -1 : 0);
    } catch (_) {
      try { document.Close(); } catch (_) {}
    }
  }

  global.partyOpsProbe = function partyOpsProbe(info) {
    var config = requireToken(info && info.token);
    var sourcePath = requireString(info.output_path, "output_path");
    var paragraphIndex = Number(info.paragraph_index || 1);
    if (!isFinite(paragraphIndex) || Math.floor(paragraphIndex) !== paragraphIndex || paragraphIndex < 1 || paragraphIndex > 100000) {
      throw new Error("参数无效：paragraph_index");
    }
    if (sourcePath !== config.output_path || paragraphIndex !== config.paragraph_index) {
      throw new Error("PARTYOPS_WPS_BRIDGE_SCOPE_INVALID");
    }

    var application = wps.WpsApplication();
    var document = null;
    var paragraph = null;
    var format = null;
    try {
      try { application.DisplayAlerts = 0; } catch (_) {}
      try { application.Visible = false; } catch (_) {}
      document = application.Documents.Open(sourcePath);
      if (!document || document.Paragraphs.Count < paragraphIndex) {
        throw new Error("WPS_PROBE_PARAGRAPH_MISSING");
      }
      paragraph = document.Paragraphs.Item(paragraphIndex);
      format = paragraph.Range.ParagraphFormat;
      format.CharacterUnitFirstLineIndent = 2;
      var measured = Number(format.CharacterUnitFirstLineIndent);
      if (Math.abs(measured - 2) > 0.001) {
        throw new Error("WPS_CHARACTER_INDENT_ROUNDTRIP_FAILED:" + measured);
      }
      document.Save();
      return {
        schema: 1,
        passed: true,
        token: info.token,
        provider: "wps",
        application: String(application.Name || "WPS Office"),
        paragraph_index: paragraphIndex,
        character_unit_first_line_indent: measured,
        visible: Boolean(application.Visible)
      };
    } finally {
      closeDocument(document, true);
      global.setTimeout(function () {
        try { application.Quit(); } catch (_) {}
      }, 500);
    }
  };
})(this);
