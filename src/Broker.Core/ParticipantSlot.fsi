namespace Broker.Core

[<Struct>]
type ScriptingClientId = ScriptingClientId of name:string

module ParticipantSlot =

    type Difficulty = int

    type ParticipantKind =
        | Human
        | BuiltInAi of difficulty:Difficulty
        | ProxyAi

    type ParticipantSlot =
        { slotIndex: int
          kind: ParticipantKind
          team: int
          boundClient: ScriptingClientId option }

    type SingleWriterError =
        | SlotAlreadyBound of slot:int * existingOwner:ScriptingClientId
        | SlotNotProxyAi of slot:int

    /// Single-writer rule (FR-009, data-model.md Invariant 1):
    /// at most one client may be bound to a given proxy-AI slot at a time;
    /// a non-`ProxyAi` slot may not carry a `boundClient` at all. Returns
    /// the first violating slot encountered or `Ok ()` if all bindings are
    /// consistent.
    val checkBindings : slots:ParticipantSlot list -> Result<unit, SingleWriterError>

    /// Try to bind a client to a slot. Fails if the slot is not `ProxyAi`,
    /// is already bound to another client, or is not present.
    val tryBind :
        slot:int
        -> client:ScriptingClientId
        -> slots:ParticipantSlot list
        -> Result<ParticipantSlot list, SingleWriterError>

    /// Remove the binding from a slot (no-op if it was unbound).
    val unbind :
        slot:int
        -> slots:ParticipantSlot list
        -> ParticipantSlot list
