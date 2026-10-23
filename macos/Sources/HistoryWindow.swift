import AppKit

final class HistoryWindow: NSWindowController, NSWindowDelegate, NSTableViewDataSource, NSTableViewDelegate {
    private let english: Bool
    private let results: [ScoreRecord]
    private let canvas = CanvasView()
    private let summary: NSTextField
    private let table = NSTableView()
    private var scroll: NSScrollView!
    private var closeButton: NSButton!
    private let dateFormatter: DateFormatter = {
        let value = DateFormatter(); value.dateFormat = "yyyy-MM-dd HH:mm"; return value
    }()
    init(store: UserStore, english: Bool) {
        self.english = english; results = store.data.results
        summary = Ui.label(store.data.results.isEmpty ? Ui.pick(english, "还没有练习记录", "No practice history yet") : Ui.pick(english, "最近 \(store.data.results.count) 次练习", "Recent sessions: \(store.data.results.count)"), color: Ui.dim)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1060, height: 540), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = Ui.pick(english, "练习记录", "Practice history")
        window.minSize = NSSize(width: 800, height: 400); window.isReleasedWhenClosed = false; window.delegate = self
        window.appearance = NSAppearance(named: .aqua)
        window.contentView = canvas
        canvas.onLayout = { [weak self] in self?.layout() }
        canvas.addSubview(summary)
        let titles = english ? ["Date", "Mode", "Text", "Chars/min", "Accuracy", "Score", "Result"] : ["日期", "模式", "文本", "速度 · 字/分", "准确率", "得分", "结果"]
        let widths: [CGFloat] = [142, 165, 210, 92, 80, 65, 158]
        for index in titles.indices {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("\(index)")); column.title = titles[index]; column.width = widths[index]; column.minWidth = 60
            table.addTableColumn(column)
        }
        table.rowHeight = 30; table.intercellSpacing = NSSize(width: 6, height: 1)
        table.usesAlternatingRowBackgroundColors = true; table.allowsMultipleSelection = false
        table.dataSource = self; table.delegate = self
        table.setAccessibilityLabel(Ui.pick(english, "练习记录", "Practice history"))
        scroll = Ui.scroll(table); scroll.hasHorizontalScroller = true; scroll.borderType = .lineBorder
        canvas.addSubview(scroll)
        closeButton = Ui.button(Ui.pick(english, "关闭", "Close"), target: self, action: #selector(closeDialog))
        closeButton.keyEquivalent = "\u{1b}"; canvas.addSubview(closeButton)
    }
    required init?(coder: NSCoder) { return nil }
    func run(parent: NSWindow) {
        guard let window = window else { return }
        window.setFrameOrigin(NSPoint(x: parent.frame.midX - window.frame.width / 2, y: parent.frame.midY - window.frame.height / 2))
        window.makeKeyAndOrderFront(nil); NSApp.runModal(for: window); window.orderOut(nil)
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { if NSApp.modalWindow === sender { NSApp.stopModal() }; return true }
    func windowWillClose(_ notification: Notification) { if NSApp.modalWindow === window { NSApp.stopModal() } }
    @objc private func closeDialog() { window?.performClose(nil) }
    private func layout() {
        let width = canvas.bounds.width, height = canvas.bounds.height
        summary.frame = NSRect(x: 18, y: 18, width: width - 36, height: 23)
        scroll.frame = NSRect(x: 18, y: 52, width: width - 36, height: height - 116)
        closeButton.frame = NSRect(x: width - 108, y: height - 47, width: 92, height: 30)
    }
    func numberOfRows(in tableView: NSTableView) -> Int { results.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard results.indices.contains(row), let key = tableColumn?.identifier.rawValue, let column = Int(key) else { return nil }
        let record = results[row], label = Ui.label(size: 11)
        switch column {
        case 0:
            let parser = ISO8601DateFormatter()
            var date = parser.date(from: record.whenIso)
            if date == nil { parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; date = parser.date(from: record.whenIso) }
            label.stringValue = date.map { dateFormatter.string(from: $0) } ?? record.whenIso
        case 1: label.stringValue = Ui.record(record.section, english) + " / " + Ui.record(record.lengthMode, english)
        case 2: label.stringValue = english && !record.titleEn.isEmpty ? record.titleEn : record.title
        case 3: label.stringValue = String(format: "%.0f", record.cpm)
        case 4: label.stringValue = String(format: "%.1f%%", record.accuracy)
        case 5: label.stringValue = "\(record.score)"
        case 6: label.stringValue = Ui.record(record.result, english)
        default: break
        }
        label.toolTip = label.stringValue
        return label
    }
}
