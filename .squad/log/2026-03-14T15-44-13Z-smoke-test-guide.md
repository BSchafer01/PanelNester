# Session: Phase 0/1 Smoke-Test Guide

**Date:** 2026-03-14T15:44:13Z  
**Agent:** Hicks

## Deliverable

Created `.squad/smoke-test-guide.md` — practical, repeatable smoke-test checklist for verifying Phase 0/1 vertical slice (import → nesting → results).

## Contents

1. Preflight build/test checks
2. Happy-path scenario with copy-paste CSV
3. Four failure-mode test cases (oversized, bad material, invalid numeric, zero quantity)
4. Expected outcomes and pass/fail criteria
5. Demo Material reference (96"×48", 0.5" edge margin, 0.0625" kerf)
6. 9-item acceptance criteria checklist

## Coverage

Exercises SC1–SC5 from Phase 0/1 test matrix across bridge, import service, nesting service, and results display. No crashes, hangs, or silent failures on error paths.

## Ready for Review

Gate for Phase 0/1 completeness: all nine acceptance items verifiable without Three.js or PDF work.
