---
name: feature-planning
description: Plan bounded StarPie features with architecture placement, compatibility rules, verification gates, worktree scope, and GPT-to-Gemini handoff contracts. Use before multi-file implementation or delegated development.
---

# StarPie Feature Planning

## Read selectively

Always read the root `AGENTS.md` and `docs/architecture.md`. Then load only the affected domain documents listed in the root routing table.

## Produce a bounded plan

Define:

- user outcome and non-goals;
- repository, isolated worktree, base ref and existing dirty state;
- owning layer and why the work belongs there;
- allowed and forbidden paths;
- compatibility, latency, memory, security/capability and localization impacts;
- implementation stages with observable deliverables;
- automated gates, independent review and human acceptance;
- completion and stop conditions, meaningful progress checks and minimum useful incomplete outcome; record numeric budgets only when the user explicitly sets them for this task. Do not impose model-call, conversation-turn, delegated-command, repair-cycle or delegation-time quotas by default.

Prefer additive, reversible changes. Reuse existing fact sources, renderers, forms, host services and action queues instead of creating parallel systems.

## GPT to Gemini handoff

The handoff must include exact worktree, base commit, model profile, task ID/revision, allowed scope, required commands, evidence format and prohibited actions. Do not authorize commit, push, merge, Tag or Release unless the user separately did so.

After the implementation request is visibly delivered and work starts, end the Codex turn. Do not poll, sleep, monitor logs or spend tokens on routine status. Resume only after the user reports completion, asks for progress, or requests troubleshooting.

Continue authorized delegation, repairs and review as needed to close the task. Do not require renewal solely because a self-imposed call, turn, command, repair-cycle or delegation-time quota was reached. Explicit user budgets, path/permission boundaries and stop conditions still apply; sustained lack of progress calls for a changed approach or a clear report, not empty retries.
