module Broker.Mvu.Tests.ViewTests

open System
open Expecto
open Broker.Core
open Broker.Mvu

[<Tests>]
let viewTests =
    testList "Broker.Mvu.View" [

        // FR-009: view is pure (same Model → same renderable string).
        test "FR-009 Synthetic_view_is_deterministic" {
            let m = Testing.Fixtures.syntheticGuestModel 2 100L
            let s1 = View.renderToString 200 60 m
            let s2 = View.renderToString 200 60 m
            Expect.equal s1 s2 "two renderToString calls on the same Model produce identical output"
        }

        // FR-011 / FR-016: off-screen render produces a non-empty string with key labels.
        test "FR-011 Synthetic_render_idle_model_contains_no_session" {
            let m = Testing.Fixtures.syntheticIdleModel (DateTimeOffset(2026, 4, 28, 11, 0, 0, TimeSpan.Zero))
            let s = View.renderToString 200 60 m
            Expect.stringContains s "Broker" "header panel renders Broker label"
            Expect.stringContains s "no session" "Idle session shows 'no session'"
            Expect.stringContains s "no clients" "empty roster shows 'no clients'"
        }

        // SC-006: dashboard for guest mode + clients + snapshot includes telemetry.
        test "FR-011 Synthetic_render_guest_mode_with_clients" {
            let m = Testing.Fixtures.syntheticGuestModel 4 25L
            let s = View.renderToString 200 60 m
            Expect.stringContains s "GUEST" "Guest mode badge appears"
            Expect.stringContains s "client-1" "first synthetic client name appears"
            Expect.stringContains s "Telemetry" "telemetry panel appears"
        }

        // FR-009 view-failure rendered as data, not exception.
        test "FR-009 Synthetic_render_does_not_throw_on_minimal_model" {
            let m = Testing.Fixtures.syntheticIdleModel DateTimeOffset.UtcNow
            let s = View.renderToString 200 60 m
            Expect.isFalse (String.IsNullOrEmpty s) "renderToString returns non-empty"
        }
    ]
