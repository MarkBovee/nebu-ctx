# public-tool-surface Specification

## Purpose
Define the canonical public MCP-tool surface for nebu-ctx after the removal of `ctx_shell`. This spec makes the new four-tool surface and the rule that `ctx_shell` is no longer a supported public tool explicit and testable.

## ADDED Requirements

### Requirement: Public nebu-ctx MCP surface exposes exactly four tools
The public MCP tool surface SHALL consist of exactly four tool names: `ctx_read`, `ctx_search`, `ctx_tree`, and `ctx`. The name `ctx_shell` SHALL NOT be exposed as a public tool.

#### Scenario: Public tool manifest is queried
- **WHEN** a client requests the list of available tools from the nebu-ctx MCP server
- **THEN** the response SHALL contain exactly four tools named `ctx_read`, `ctx_search`, `ctx_tree`, and `ctx`
- **AND** the response SHALL NOT contain a tool named `ctx_shell`

#### Scenario: Agent calls a removed public tool
- **WHEN** an agent invokes the MCP server with tool name `ctx_shell`
- **THEN** the server SHALL respond with a clear invalid-params error indicating that `ctx_shell` is no longer part of the public surface
- **AND** the error message SHALL NOT recommend an alternative nebu-ctx shell tool because none exists

#### Scenario: Public surface size assertions
- **WHEN** a unit test enumerates the unified public tool definitions
- **THEN** the test SHALL assert the tool count is exactly four
- **AND** the test SHALL assert the tool name list equals `["ctx_read", "ctx_search", "ctx_tree", "ctx"]`

### Requirement: Public guidance does not map shell actions to a nebu-ctx tool
Public nebu-ctx guidance SHALL NOT recommend a nebu-ctx tool for shell or bash actions. The guidance SHALL direct agents to use their native shell or bash tooling for those actions.

#### Scenario: Guidance mentions shell actions
- **WHEN** any generated instruction, rules, or guidance surface describes how to perform a shell or bash action
- **THEN** the text SHALL recommend the agent's native shell or bash tooling
- **AND** the text SHALL NOT mention `ctx_shell` as a recommendation

#### Scenario: Guidance preference table excludes shell
- **WHEN** any generated guidance surface lists preferred nebu-ctx tools over native equivalents in a table
- **THEN** the table SHALL contain entries only for the four public tools (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx`)
- **AND** the table SHALL NOT contain a row that maps a native shell tool to a nebu-ctx tool
