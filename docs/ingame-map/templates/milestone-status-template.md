# Milestone Status Entry Template

Add this structure to `docs/development-status.md` after each milestone:

```markdown
## In-game client milestone N — <name>

Status: Complete / Partial / Blocked

### Completed

- ...

### Verification

- Build:
- Automated tests:
- Manual tests:

### Known limitations

- ...

### Source-sensitive targets

- ...

### Blockers

- None / ...

### Next step

- ...
```

For a manual-test blocker, use:

```text
Status: Partial — awaiting manual verification
```

and include exact steps, expected observations, profile-backup requirements, and what implementation decision depends on the result.
