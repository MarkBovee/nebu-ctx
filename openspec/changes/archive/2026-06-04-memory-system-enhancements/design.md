## Context

The nebu-ctx brain/memory system currently provides:
- Session-scoped temporary memory (brain) via `ctx_session` tool
- Canonical persistent knowledge (knowledge) via `ctx_knowledge` tool
- Both are PostgreSQL-backed in production with in-memory fallbacks for testing
- Memory entries are stored as key-value pairs with metadata (confidence, lifecycle, source, etc.)
- Analytics tools (`ctx_gain`, `ctx_cost`, etc.) already consume telemetry from tool usage

Current limitations observed:
1. Session metadata shows `tool_calls: 0` despite analytics recording 18k+ calls - tracking inconsistency
2. Memory recall (`ctx memory recall`) requires specific queries; no browsing capability
3. No visibility into memory lifecycle (promotion candidates, stale entries)
4. No traceability between brain session events and promoted knowledge facts
5. No export/import for knowledge portability between environments

## Goals / Non-Goals

**Goals:**
- Fix session tool call tracking to accurately reflect usage
- Add memory browsing/listing with filtering (category, time, source, etc.)
- Implement contextual memory surfacing during related tool use via hooks
- Add lifecycle inspection commands (promotion candidates, stale memories)
- Show traceability links between brain events and knowledge facts
- Enable JSON-based memory export/import for portability
- Maintain backward compatibility with existing memory APIs
- Leverage existing storage and tool handler patterns

**Non-Goals:**
- Changing the core client-server architecture
- Modifying the PostgreSQL-backed storage model
- Altering the fundamental brain vs knowledge distinction
- Adding real-time sync or collaboration features
- Modifying the analytics tool implementations
- Changing the public MCP 5-tool surface

## Decisions

### Session Tracking Fix
- **Problem**: Session tool_calls not incrementing in metadata despite analytics tracking
- **Solution**: Ensure `session.record_tool_receipt()` properly updates the session stats
- **Rationale**: Analytics already work correctly; fix is likely in session state persistence
- **Alternative Considered**: Rebuilding tracking from analytics - rejected as redundant and less performant

### Memory Browsing Implementation
- **Problem**: Recall requires specific queries; discovery difficult
- **Solution**: Add `ctx memory list` subcommand with filtering options (--category, --since, --source-type, --limit)
- **Rationale**: Follows existing CLI patterns; leverages existing store listing methods
- **Alternative Considered**: GraphQL-like query language - rejected as overkill for current scale

### Contextual Memory Surfacing
- **Problem**: Users must manually recall relevant memories
- **Solution**: Enhance hook system to suggest memories during related tool use (e.g., when running shell commands related to past issues)
- **Rationale**: Uses existing hook infrastructure; passive assistance improves workflow
- **Alternative Considered**: Always-show sidebar - rejected as potentially distracting

### Lifecycle Transparency
- **Problem**: No visibility into memory health or promotion readiness
- **Solution**: Add `ctx memory lifecycle` subcommand showing stats and candidates
- **Rationale**: Builds on existing lifecycle_status and confidence fields
- **Alternative Considered**: Automatic promotion - rejected as requires human judgment for canonical facts

### Cross-Domain Correlation
- **Problem**: Hard to trace how session events led to canonical knowledge
- **Solution**: Enhance knowledge facts to include promotion_identity linking to source brain events
- **Rationale**: Already have promotion_identity field; just needs exposure in recall results
- **Alternative Considered**: Separate audit log - rejected as duplicates existing traceability

### Memory Portability
- **Problem**: Knowledge tied to specific PostgreSQL instance
- **Solution**: Add export/import commands producing/consuming JSON representations
- **Rationale**: Simplest approach for knowledge transfer between environments
- **Alternative Considered**: Direct database replication - rejected as couples to specific storage tech

## Risks / Trade-offs

[Session tracking fix] → Risk: May reveal other inconsistencies in session state → Mitigation: Test thoroughly with various tool combinations

[Memory browsing] → Risk: Large result sets could overwhelm clients → Mitigation: Default limits, pagination options, and existing compression mechanisms

[Contextual surfacing] → Risk: Suggestions could be distracting or inaccurate → Mitigation: Configurable via settings, relevance scoring thresholds, opt-out

[Lifecycle transparency] → Risk: May expose internal implementation details → Mitigation: Carefully curate what information is exposed (focus on actionable insights)

[Cross-domain correlation] → Risk: Increased response size for knowledge recalls → Mitigation: Optional field, only include when relevant data exists

[Memory portability] → Risk: JSON format may not capture all nuances → Mitigation: Focus on essential fields; document limitations

## Open Questions

1. Should contextual memory surfacing be opt-in or opt-out by default?
2. What default limits should apply to memory browsing queries?
3. Should memory export include session-specific brain data or only canonical knowledge?
4. How should temporal filtering handle timezone differences in distributed teams?
