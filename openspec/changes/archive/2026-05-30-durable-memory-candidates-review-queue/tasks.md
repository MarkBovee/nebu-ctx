## 1. Hosted candidate queue foundation

- [x] 1.1 Add server-side contracts and store support for project-scoped memory candidates with bounded review statuses, confidence, classification, evidence, and deterministic identity fields
- [x] 1.2 Add hosted memory service and tool actions for candidate ingest, candidate listing, and candidate review decisions while keeping hosted knowledge as the canonical fact store
- [x] 1.3 Reuse existing promotion identity and deduplication patterns so replayed candidate submissions do not create duplicate current candidate or knowledge records

## 2. Client-side candidate extraction and submission

- [x] 2.1 Add deterministic candidate extraction over session findings, decisions, assistant conclusions, journal events, and verification evidence in the client memory pipeline
- [x] 2.2 Implement confidence scoring and classification for durable debugging fact types such as root cause, runtime caveat, verified behavior, contract decision, and live verification
- [x] 2.3 Submit extracted candidates during stop or idle flush, with local outbox fallback for offline-safe candidate writes and review actions

## 3. Promotion, ranking, and wake-up behavior

- [x] 3.1 Implement confidence-band handling so high-confidence candidates auto-promote, medium-confidence candidates enter the hosted review queue, and low-confidence candidates stay non-canonical
- [x] 3.2 Update hosted recall and wake-up ranking so durable debugging fact types rank ahead of lower-value generic facts when relevant project tokens match
- [x] 3.3 Ensure candidate and canonical fact deduplication prevents repeated conclusions from creating duplicate active records across multiple sessions

## 4. Dashboard project-memory ergonomics

- [x] 4.1 Extend dashboard memory contracts and payload composition to include bounded candidate review data and promotion summaries on `/api/dashboard/projects/{projectId}/memory`
- [x] 4.2 Add dashboard admin endpoints or route extensions for candidate acceptance and rejection without fragmenting the existing project-memory workflow
- [x] 4.3 Keep dashboard responses bounded and operator-friendly by exposing candidate counts, statuses, and evidence metadata instead of unbounded history blobs

## 5. Verification

- [x] 5.1 Add client tests for deterministic candidate extraction, confidence thresholds, offline queueing, and replay-safe identity behavior
- [x] 5.2 Add server integration tests for candidate ingest, auto-promotion, pending review behavior, deduplication, and candidate review decisions
- [x] 5.3 Add dashboard integration tests for project-memory candidate visibility, promotion summaries, and wake-up ranking of durable debugging facts
