"""只向 UUID 已绑定的 QEMU Guest 发送按键，供原版安装器恢复和配置使用。"""
from __future__ import annotations

import time

from providers import qmp


def key(lab, target: str, chord: str) -> None:
    state = lab.state(target)
    if not lab.live(state):
        raise RuntimeError("OWNED_GUEST_NOT_RUNNING")
    qmp(state["qmp_port"], "send-key", {"keys": [
        {"type": "qcode", "data": item} for item in chord.split("-")], "hold-time": 10})
    time.sleep(0.035)


def type_ascii(lab, target: str, text: str) -> None:
    """英文键盘布局；拒绝不支持字符，避免静默输入错误命令。"""
    mapping = {" ": "spc", "\n": "ret", ".": "dot", ",": "comma", "/": "slash",
               "\\": "backslash", ":": "shift-semicolon", ";": "semicolon",
               "-": "minus", "_": "shift-minus", "=": "equal", "+": "shift-equal",
               "'": "apostrophe", '"': "shift-apostrophe", "[": "bracket_left",
               "]": "bracket_right", "{": "shift-bracket_left", "}": "shift-bracket_right",
               "<": "shift-comma", ">": "shift-dot", "?": "shift-slash", "|": "shift-backslash",
               "`": "grave_accent", "~": "shift-grave_accent"}
    mapping.update({char: "shift-" + str(index + 1) for index, char in enumerate("!@#$%^&*(")})
    mapping[")"] = "shift-0"
    chords = []
    for char in text:
        if char.isascii() and char.isalnum():
            chords.append(("shift-" if char.isupper() else "") + char.lower())
        elif char in mapping:
            chords.append(mapping[char])
        else:
            raise ValueError("CONSOLE_REQUIRES_ASCII_KEYBOARD_INPUT")
    for chord in chords:
        key(lab, target, chord)
