# AGENTS.md — Cipanguanli / 磁盘空间诊断器

This repository inherits the cross-project AI workflow from:

`https://github.com/zcssr1998-art/AI-Development-Rules/blob/main/GLOBAL_AI_RULES.md`

This file contains **Cipanguanli-specific** rules only. Rule precedence:

`explicit current user instruction > this AGENTS.md > GLOBAL_AI_RULES.md > agent defaults`

## Product goal

This is a Windows 10/11 x64 local disk-space diagnosis tool. Its job is to explain **what uses space, who owns it, whether it is safe to change, and whether cleanup/migration/official Windows mechanisms should be used**. It is not a blind deletion tool.

## Safety boundaries

Treat destructive filesystem behavior as high risk.

- Never directly delete WinSxS, Windows Installer cache, DriverStore, WindowsApps, System Volume Information/VSS, pagefile/swapfile, or other protected Windows components merely because they are large.
- Prefer Windows-supported mechanisms such as DISM, Settings/System Protection, documented cache cleanup, or explicit user-confirmed actions.
- Any action that can materially affect boot, hibernation, restore points, installed applications, drivers, or system integrity must be clearly distinguished from low-risk cache cleanup.
- Preserve the product's risk score/explanation model; do not turn recommendations into silent destructive automation.
- Administrator-required actions need explicit handling and must fail safely when privileges are unavailable.

## Verification

For changes affecting cleanup recommendations or actions, verify both:

- classification/explanation is correct for the target path/category;
- protected/high-risk paths cannot be accidentally treated as ordinary deletable cache.

Use representative Windows-path fixtures/tests where real destructive runtime testing would be unsafe.

## Repository notes

- Preserve `THIRD_PARTY_NOTICES.md` and license obligations when reusing external code.
- Do not commit machine-specific scan output, private local paths containing personal data, credentials, or secrets.

## Reporting additions

Follow the global concise-reporting rule. For this project include only material safety behavior, tests, and any unverified Windows-runtime risk.
