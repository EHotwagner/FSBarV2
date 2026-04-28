namespace Broker.Core

module Mode =

    type Mode =
        | Idle
        | Hosting of Lobby.LobbyConfig
        | Guest

    /// True iff admin commands may be issued in this mode.
    /// (FR-004, FR-016: only `Hosting` permits admin authority.)
    val isAdminAuthorised : Mode -> bool

    /// Validate a transition. Allowed transitions (per data-model.md §1.2):
    ///   Idle -> Hosting | Guest
    ///   Hosting | Guest -> Idle
    /// Any other transition returns Error with a human-readable reason.
    val transition : current:Mode -> next:Mode -> Result<Mode, string>
