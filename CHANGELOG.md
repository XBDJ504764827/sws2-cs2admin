# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Map ratings

- Show independent CKZ/VNL ratings in the text-only map list using the shared mapmanager catalog.

### Sidebar revision

- Switch `!admin` to persistent left categories and a right content pane, with the first permitted module opened automatically.
- Remove decorative player/map/warning glyphs while keeping destructive actions visibly styled as text.

### Changed

- Open `!admin` as a two-module Custom HUD dashboard: Player actions and Map management.
- Read players/maps when entering a module; place kick and ban first in the action list.
- Show explicit kick, ban and map-change confirmation details and destructive action styling.
- Require `sws2.admin.map` for map changes (included in `sws2.admin.*`).

### Fixed

- Resolve menu visibility through the admin service so admins.jsonc-only permissions work.
- Recheck map permission before countdown and again before requesting a map change.
- Use a dedicated ban reason instead of the kick reason for menu bans.
- Align dependency versions with SwiftlyS2 1.5.2 to resolve package downgrades.

- Close the Simplified Chinese translation JSON after adding the missing-map message.

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
