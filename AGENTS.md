Local operational rule:
- Do not run `dotnet build`, solution builds, or full project builds from Codex in this repository unless the user explicitly asks for it in the current turn. Build commands hang without useful output in the sandbox; prefer static inspection and targeted non-build checks such as `git diff --check`.

@./.agents/project_concepts.md
@./.agents/agent_code_rules.md
@./.agents/module_catalog.md
