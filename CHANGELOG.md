# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

## [1.0.0] - 2026-10-26

### Added

- Initial release of `sws2-cs2admin`.
- Admin identities, groups, direct permission nodes and immunity from `admins.jsonc`.
- Optional admin nodes from SwiftlyS2's `permissions.jsonc`.
- Player actions: kick, slay, slap, respawn and freeze.
- Communication controls: mute, gag, silence and removals with expiry handling.
- Persistent temporary and permanent bans with join checks and offline `addban`.
- JSONL audit logging with daily files and retention.
- Admin panel built on the shared `sws2-cs2menus` service.
- Shared `IAdminService` contract for other plugins.
- English and Simplified Chinese translations.
- Build, CI, PR pre-release and main-branch release workflows.
