namespace Broker.Tui

module Layout =

    /// Slot identifiers used by `DashboardView.render` to populate panes.
    type Slot =
        | Header
        | BrokerPane
        | SessionPane
        | ClientsPane
        | TelemetryPane
        | Footer

    /// Build the root Spectre.Console layout the TUI tick loop refreshes
    /// each tick. Panes are addressable by `Slot` so `DashboardView.render`
    /// can update individual sections without rebuilding the whole tree.
    val rootLayout : unit -> Spectre.Console.Layout

    /// Look up a child layout by slot. Returns `None` if the layout was
    /// produced by something other than `rootLayout`.
    val tryGetSlot : root:Spectre.Console.Layout -> slot:Slot -> Spectre.Console.Layout option
