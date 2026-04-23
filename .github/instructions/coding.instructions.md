---
applyTo: '**'
---

# Coding Standards

- DRY and SOLID
- Prefer small functions and clear naming
- For C#: avoid fully-qualified type names and avoid `dynamic`
- Build with zero errors/warnings and keep tests passing
---
applyTo: '**'
---

# Coding Standards

## Core Principles

- **DRY**: Before adding code, check if similar functionality exists. Refactor 3+ duplications into shared components.
- **SOLID**: Single responsibility, open/closed, Liskov substitution, interface segregation, dependency inversion.
- **Small functions**: Prefer small focused helpers with a single level of abstraction. Orchestrator methods may be larger when coordinating phases, but must delegate real work to named helpers.
- **Pure functions**: Prefer functions without side effects when possible.
- **Meaningful names**: Use descriptive, intention-revealing names for all identifiers.

## Senior Delivery Patterns

These patterns reflect the preferred implementation style in this repository:

- **Domain naming over transport naming**: Rename models and helpers to business language (for example provider teams -> brands) when that improves intent and downstream readability.
- **Structured diagnostics**: Log with business identifiers (dealer code, account code, provider user key) and operation context to make production troubleshooting deterministic.
- **Refactor by extraction**: Reduce large services by moving object-building and report-aggregation logic into dedicated builders/helpers while keeping behavior unchanged.
- **Keep generic builders generic**: Infrastructure helpers such as `QueryBuilder` may assemble reusable query templates, but domain-specific field names, normalization rules, and business query combinations must stay in products helpers/injectors/services instead of adding `BuildBrand*`-style methods there.
- **Prefer direct config lookups for simple settings**: For small, single-use configuration values, prefer direct `Application.Configuration["Section:Key"]` access in the consuming code over adding dedicated settings/options classes. Introduce typed settings models only when multiple consumers or repeated structure justify the extra code.

## C# Specific Rules

### Integration Test Response Typing (Mandatory)
- In integration tests, API responses **must** use concrete client/service models (for example `SearchResultSet`) and be validated through those models.
- `ApiJsonRequestAsync<object>` is forbidden in tests.
- `ApiJsonRequestAsync<JsonElement>` is forbidden in tests when a real model exists.
- If a real model does not exist, create/reuse a strongly typed DTO first, then use that DTO in the test.
- Treat model-based validation as mandatory for Aspire integration tests and legacy integration tests alike.

## C# Specific Rules

### No Fully Qualified Type Names
Always add `using` directives and use short type names. Only exception: disambiguation of same-named types.
```csharp
// Bad: System.Collections.Generic.List<string>
// Good: List<string> (with using System.Collections.Generic;)
```

### No Long Parameter Lists
- 3+ parameters → use a model/DTO/request object.
```csharp
// Bad: CreateDealer(string name, string email, string phone, string address)
// Good: CreateDealer(CreateDealerRequest request)
```

### Parameter formatting
- Keep the full parameter list for methods and functions on a single line when it fits within the project's line-length policy. Do not place each parameter on its own line.
- If the parameter list is long, prefer creating a request/DTO object instead of breaking parameters across multiple lines.

### Method invocation formatting
- Keep method invocations on a single line when they fit within the project's line-length policy.
- Prefer this especially for fluent/assertion/test calls to keep intent readable at a glance.
- If an invocation is too long, break at logical argument boundaries.

### Variable naming
- Use informative, intention-revealing variable names (e.g. `companyGroupCode`, `updatedMarketplaceSettings`) instead of generic names (e.g. `code`, `updated`, `result`).
- Avoid ambiguous short names unless they are conventional loop variables in a very small local scope.

### No `dynamic`
Use strongly-typed classes, `object` with safe casting, or `JsonElement`/`JObject` for JSON processing. `dynamic` is forbidden.

### Constructor Optimization
When adding properties that touch many files, prefer optional parameters with defaults, factory methods, or builder patterns over requiring changes everywhere.

### XML Documentation Comments
- Add XML documentation comments (`///`) to **all** methods, classes, records, and helper functions.
- This includes `public`, `internal`, and `private` members, including static helper functions.
- Keep comments concise and useful: summarize intent, key parameters, and return behavior where relevant.
- For each parameter, include a brief description of its purpose and any constraints.
- For return values, describe what is returned and what callers should rely on.

### Helpful Inline Comments
- Add a short inline intent comment for non-obvious logic blocks and handlers where behavior is not immediately clear from naming alone.
- Apply this especially to minimal API endpoint handlers and protocol dispatch sections
- Keep comments brief and focused on why/intent, not line-by-line narration.

### Helper Functions & Subfunctions
- All helper, utility, and static functions require comprehensive XML documentation with `<summary>`, `<param>`, and `<returns>` tags.
- Document the function's role in the larger workflow and any preconditions or side effects.
- For multi-parameter helpers, clarify relationships between parameters and their intended usage patterns.
- Include inline comments that explain non-obvious algorithms, protocols, or business logic.

### Control Flow and Method Structure
- Prefer `switch`/pattern matching over long `if/else if` chains for command dispatch, protocol handlers, and status routing.
- Keep methods focused and short; split orchestration methods into small private helper methods with clear names.
- Minimize deep nesting: use guard clauses and early returns to keep logic flat and readable.
- For methods that can grow over time (for example `Execute*`, `Handle*`, endpoint dispatch), design for extensibility with per-case helpers.

### System.Text.Json/OpenAPI Required Properties
- Do not combine `[Required]` with non-public setters/getters unless the member is explicitly included for STJ metadata generation.
- For models used by OpenAPI schema generation, if a property must remain `internal set` or has a non-public accessor, add `[JsonInclude]` to prevent runtime failures like: `JsonPropertyInfo ... is marked required but does not specify a setter`.
- Prefer public setters for required request/response DTO properties unless encapsulation is intentionally required.

## EF Core Best Practices

- **Centralize timestamps** in `DbContext.SaveChanges()` override — set `SysAdd` on insert, `SysMod` on insert+update. Never manage timestamps in business logic.
- **Fluent API**: Use `HasDefaultValueSql("GETUTCDATE()")` for database-level defaults.
- **Bulk operations**: Manually set timestamps before `BulkInsertAsync()`/`BulkUpdateAsync()` since they bypass change tracking.

## Error Handling & Performance

- **Fail fast**: Validate inputs early with clear error messages.
- **Resource management**: Use `using` statements for disposable resources.
- **Lazy loading**: Don't compute values until needed.
- **Caching**: Cache expensive computations and frequently accessed data.

## Quality Checklist

After implementing changes, verify:
- [ ] No code duplication introduced
- [ ] Performance impact acceptable
- [ ] Error handling comprehensive
- [ ] Build: 0 errors, 0 warnings
- [ ] All tests passing
- [ ] External integration changes include dry-run and idempotency coverage
- [ ] Code is self-documenting or has "why" comments where needed
