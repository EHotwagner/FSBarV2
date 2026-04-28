namespace Broker.Tui

open Spectre.Console

module Layout =

    type Slot =
        | Header
        | BrokerPane
        | SessionPane
        | ClientsPane
        | TelemetryPane
        | Footer

    let private nameOf (slot: Slot) : string =
        match slot with
        | Header        -> "Header"
        | BrokerPane    -> "BrokerPane"
        | SessionPane   -> "SessionPane"
        | ClientsPane   -> "ClientsPane"
        | TelemetryPane -> "TelemetryPane"
        | Footer        -> "Footer"

    let rootLayout () : Layout =
        let body =
            (Layout("Body"))
                .SplitColumns(
                    (Layout(nameOf BrokerPane)).Ratio(1),
                    (Layout(nameOf SessionPane)).Ratio(1),
                    (Layout(nameOf ClientsPane)).Ratio(1))

        let bottom =
            (Layout("Bottom"))
                .SplitRows(
                    body.Ratio(2),
                    (Layout(nameOf TelemetryPane)).Ratio(2))

        let root =
            (Layout("Root"))
                .SplitRows(
                    (Layout(nameOf Header)).Size(3),
                    bottom,
                    (Layout(nameOf Footer)).Size(3))
        root

    let tryGetSlot (root: Layout) (slot: Slot) : Layout option =
        try Some (root.[nameOf slot])
        with _ -> None
