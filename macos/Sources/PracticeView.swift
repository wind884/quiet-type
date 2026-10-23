import AppKit

final class PracticeView: FlippedView, NSTextInputClient {
    private struct Cell { var column: Int; var row: Int; var span: Int; var line: Int }
    private(set) var session: TypingSession?
    var onChange: (() -> Void)?
    var isCode = false
    var inputAllowed = true { didSet { updateActivity(); needsDisplay = true } }
    var maskReference = false { didSet { needsDisplay = true } }
    var ghostPosition = -1 { didSet { needsDisplay = true } }
    var textSize: Int = 15 { didSet { textSize = min(21, max(13, textSize)); cachedColumns = -1; followCaret = true; needsDisplay = true } }
    private var cells: [Cell] = []
    private var cachedColumns = -1
    private var scrollRow = 0
    private var followCaret = true
    private var marked = ""
    private var markedSelection = NSRange(location: 0, length: 0)
    private var blink = true
    private var blinkTimer: Timer?
    private var latinFont: NSFont { .monospacedSystemFont(ofSize: CGFloat(textSize), weight: .regular) }
    private var cjkFont: NSFont { .systemFont(ofSize: CGFloat(textSize)) }
    private var cellWidth: CGFloat { ceil(("M" as NSString).size(withAttributes: [.font: latinFont]).width) }
    private var lineHeight: CGFloat { CGFloat(textSize + 16) }
    private var gutter: CGFloat { isCode ? 40 : 0 }
    private var visibleRows: Int { max(1, Int((bounds.height - 12) / lineHeight)) }
    override var acceptsFirstResponder: Bool { true }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        setAccessibilityRole(.textArea)
        setAccessibilityLabel("Typing area")
        blinkTimer = Timer.scheduledTimer(withTimeInterval: 0.53, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            self.blink.toggle()
            if self.window?.firstResponder === self { self.needsDisplay = true }
        }
    }
    required init?(coder: NSCoder) { return nil }
    deinit { blinkTimer?.invalidate() }

    func load(_ value: TypingSession, isCode: Bool) {
        discardComposition()
        self.isCode = isCode; session = value
        cells = []; cachedColumns = -1; scrollRow = 0; followCaret = true
        ghostPosition = -1; maskReference = false
        updateActivity(); changed()
    }
    func clear() {
        session?.setActive(false); discardComposition()
        session = nil; cells = []; cachedColumns = -1
        setAccessibilityValue(""); needsDisplay = true
    }
    func focusInput() {
        if inputAllowed { window?.makeFirstResponder(self) }
        updateActivity(); blink = true; needsDisplay = true
    }
    func updateActivity() {
        session?.setActive(inputAllowed && window?.isKeyWindow == true && window?.firstResponder === self)
        needsDisplay = true
    }
    override func becomeFirstResponder() -> Bool {
        DispatchQueue.main.async { [weak self] in self?.updateActivity() }
        return true
    }
    override func resignFirstResponder() -> Bool { session?.setActive(false); needsDisplay = true; return true }
    override func mouseDown(with event: NSEvent) { focusInput() }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .iBeam) }
    override func setFrameSize(_ newSize: NSSize) { super.setFrameSize(newSize); cachedColumns = -1; followCaret = true }
    override func scrollWheel(with event: NSEvent) {
        let delta = Int(event.scrollingDeltaY / (event.hasPreciseScrollingDeltas ? 12 : 1))
        scrollRow = min(max(0, (cells.last?.row ?? 0) - visibleRows + 1), max(0, scrollRow - delta))
        followCaret = false; needsDisplay = true
        inputContext?.invalidateCharacterCoordinates()
    }
    private func changed() {
        followCaret = true; blink = true; needsDisplay = true
        setAccessibilityValue(session?.typed ?? "")
        NSAccessibility.post(element: self, notification: .valueChanged)
        inputContext?.invalidateCharacterCoordinates()
        onChange?()
    }
    override func keyDown(with event: NSEvent) {
        guard inputAllowed, session != nil else { return }
        if event.modifierFlags.contains(.command) && event.charactersIgnoringModifiers?.lowercased() == "v" { NSSound.beep(); return }
        if event.keyCode == 51 && !hasMarkedText() && !event.modifierFlags.intersection([.command, .option]).isEmpty {
            if session?.backspace(word: true) == true { changed() }
            return
        }
        interpretKeyEvents([event])
    }
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if event.modifierFlags.contains(.command), event.charactersIgnoringModifiers?.lowercased() == "v" { NSSound.beep(); return true }
        return super.performKeyEquivalent(with: event)
    }
    override func doCommand(by selector: Selector) {
        guard inputAllowed, let value = session else { return }
        let name = NSStringFromSelector(selector)
        var edited = false
        switch name {
        case "deleteBackward:": edited = value.backspace()
        case "deleteWordBackward:", "deleteToBeginningOfLine:": edited = value.backspace(word: true)
        case "insertNewline:", "insertLineBreak:": if isCode { edited = value.commit("\n") }
        case "insertTab:", "insertTabIgnoringFieldEditor:":
            if isCode {
                var count = 0
                while count < 4 && value.position + count < value.targetUnits.count && value.targetUnits[value.position + count] == " " { count += 1 }
                edited = value.commit(String(repeating: " ", count: max(1, count)))
            } else { window?.selectNextKeyView(self) }
        case "cancelOperation:": discardComposition()
        default: break
        }
        if edited { changed() }
    }
    func insertText(_ string: Any, replacementRange: NSRange) {
        let text = (string as? NSAttributedString)?.string ?? (string as? String ?? "")
        marked = ""; markedSelection = NSRange(location: 0, length: 0)
        guard inputAllowed, let value = session, !value.isFinished else { needsDisplay = true; return }
        if value.commit(text) { changed() } else { needsDisplay = true }
    }
    func setMarkedText(_ string: Any, selectedRange: NSRange, replacementRange: NSRange) {
        guard inputAllowed, session?.isFinished == false else { return }
        marked = (string as? NSAttributedString)?.string ?? (string as? String ?? "")
        let length = (marked as NSString).length
        let location = selectedRange.location == NSNotFound ? length : max(0, min(selectedRange.location, length))
        markedSelection = NSRange(location: location, length: max(0, min(selectedRange.length, length - location)))
        blink = true; followCaret = true; needsDisplay = true
        inputContext?.invalidateCharacterCoordinates()
    }
    func unmarkText() {
        let pending = marked
        marked = ""; markedSelection = NSRange(location: 0, length: 0)
        if !pending.isEmpty { insertText(pending, replacementRange: NSRange(location: NSNotFound, length: 0)) }
        else { needsDisplay = true }
    }
    private func discardComposition() {
        marked = ""; markedSelection = NSRange(location: 0, length: 0)
        inputContext?.discardMarkedText()
        needsDisplay = true
    }
    func hasMarkedText() -> Bool { !marked.isEmpty }
    func markedRange() -> NSRange { hasMarkedText() ? NSRange(location: ((session?.typed ?? "") as NSString).length, length: (marked as NSString).length) : NSRange(location: NSNotFound, length: 0) }
    func selectedRange() -> NSRange { NSRange(location: ((session?.typed ?? "") as NSString).length + markedSelection.location, length: markedSelection.length) }
    func validAttributesForMarkedText() -> [NSAttributedString.Key] { [.underlineStyle, .foregroundColor, .backgroundColor, .markedClauseSegment] }
    func attributedSubstring(forProposedRange range: NSRange, actualRange: NSRangePointer?) -> NSAttributedString? {
        let value = (session?.typed ?? "") + marked
        let length = (value as NSString).length
        guard range.location != NSNotFound, range.location >= 0, range.location <= length else { return nil }
        let valid = NSRange(location: range.location, length: max(0, min(range.length, length - range.location)))
        actualRange?.pointee = valid
        return NSAttributedString(string: (value as NSString).substring(with: valid), attributes: [.font: cjkFont])
    }
    func firstRect(forCharacterRange range: NSRange, actualRange: NSRangePointer?) -> NSRect {
        ensureLayout()
        actualRange?.pointee = selectedRange()
        let rect = caretRect()
        let inWindow = convert(rect, to: nil)
        return window?.convertToScreen(inWindow) ?? inWindow
    }
    func characterIndex(for point: NSPoint) -> Int { ((session?.typed ?? "") as NSString).length }

    private func span(_ text: String) -> Int { PracticeText.cellSpan(text) }
    private func isWord(_ text: String) -> Bool { PracticeText.isWord(text) }
    private func ensureLayout() {
        guard let value = session else { return }
        let columns = max(2, Int((bounds.width - gutter - 22) / cellWidth))
        if columns != cachedColumns || cells.count != value.targetUnits.count + 1 {
            cachedColumns = columns; cells = []
            var column = 0, row = 0, line = 1
            for (index, unit) in value.targetUnits.enumerated() {
                let width = span(unit)
                if unit != "\n" {
                    if isWord(unit) && (index == 0 || !isWord(value.targetUnits[index - 1])) {
                        var length = 0, j = index
                        while j < value.targetUnits.count && isWord(value.targetUnits[j]) { length += span(value.targetUnits[j]); j += 1 }
                        if length <= columns && column > 0 && column + length > columns { row += 1; column = 0 }
                    }
                    if column + width > columns { row += 1; column = 0 }
                }
                cells.append(Cell(column: column, row: row, span: width, line: line))
                if unit == "\n" { row += 1; column = 0; line += 1 } else { column += width }
            }
            if column >= columns { row += 1; column = 0 }
            cells.append(Cell(column: column, row: row, span: 1, line: line))
        }
        if followCaret, let caret = cells.indices.contains(value.position) ? cells[value.position] : cells.last {
            if caret.row < scrollRow { scrollRow = caret.row }
            if caret.row >= scrollRow + visibleRows { scrollRow = caret.row - visibleRows + 1 }
            followCaret = false
        }
        scrollRow = max(0, min(scrollRow, max(0, (cells.last?.row ?? 0) - visibleRows + 1)))
    }
    private func rect(_ cell: Cell) -> NSRect { NSRect(x: 8 + gutter + CGFloat(cell.column) * cellWidth, y: 7 + CGFloat(cell.row - scrollRow) * lineHeight, width: CGFloat(cell.span) * cellWidth, height: lineHeight) }
    private func caretRect() -> NSRect {
        guard let value = session, cells.indices.contains(value.position) else { return NSRect(x: 8, y: 7, width: 1, height: lineHeight) }
        var result = rect(cells[value.position]); result.size.width = 1; return result
    }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.white.setFill(); dirtyRect.fill()
        guard let value = session else { return }
        ensureLayout()
        for index in value.targetUnits.indices {
            let cell = cells[index]
            guard cell.row >= scrollRow, cell.row < scrollRow + visibleRows else { continue }
            let area = rect(cell)
            if isCode && (index == 0 || value.targetUnits[index - 1] == "\n") {
                let line = "\(cell.line)" as NSString
                let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedSystemFont(ofSize: 10, weight: .regular), .foregroundColor: NSColor(calibratedWhite: 0.72, alpha: 1)]
                line.draw(at: NSPoint(x: 3, y: area.minY + 4), withAttributes: attrs)
            }
            let entered = index < value.position
            var unit = entered ? value.inputUnits[index] : value.targetUnits[index]
            if unit == "\n" { unit = "↵" }
            if !entered && maskReference && unit != " " { unit = "_" }
            let font = span(unit) > 1 ? cjkFont : latinFont
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: area).addClip()
            (unit as NSString).draw(at: area.origin, withAttributes: [.font: font, .foregroundColor: entered ? Ui.ink : NSColor(calibratedWhite: 0.78, alpha: 1), .ligature: 0])
            if entered && value.inputUnits[index] != value.targetUnits[index] {
                NSColor(calibratedRed: 0.74, green: 0.39, blue: 0.39, alpha: 1).setFill()
                NSRect(x: area.minX, y: area.minY + CGFloat(textSize) + 5, width: max(2, area.width - 2), height: 1).fill()
            }
            NSGraphicsContext.restoreGraphicsState()
        }
        let caret = caretRect()
        if window?.firstResponder === self && !value.isFinished && inputAllowed {
            if !marked.isEmpty {
                let width = max(cellWidth * 4, (marked as NSString).size(withAttributes: [.font: cjkFont]).width + 6)
                let preview = NSRect(x: caret.minX, y: caret.minY, width: min(width, bounds.width - caret.minX), height: lineHeight)
                NSColor.white.setFill(); preview.fill()
                (marked as NSString).draw(at: preview.origin, withAttributes: [.font: cjkFont, .foregroundColor: Ui.ink, .underlineStyle: NSUnderlineStyle.single.rawValue])
            } else if blink {
                Ui.ink.setFill(); NSRect(x: caret.minX, y: caret.minY + 1, width: 1, height: CGFloat(textSize + 3)).fill()
            }
        }
        if cells.indices.contains(ghostPosition) {
            let ghost = rect(cells[ghostPosition])
            let path = NSBezierPath(); path.setLineDash([2, 2], count: 2, phase: 0)
            path.move(to: ghost.origin); path.line(to: NSPoint(x: ghost.minX, y: ghost.minY + CGFloat(textSize + 4)))
            Ui.accent.setStroke(); path.stroke()
        }
        let rows = (cells.last?.row ?? 0) + 1
        if rows > visibleRows {
            let track = bounds.height - 14
            let height = max(20, track * CGFloat(visibleRows) / CGFloat(rows))
            let y = 7 + (track - height) * CGFloat(scrollRow) / CGFloat(max(1, rows - visibleRows))
            NSColor(calibratedWhite: 0.84, alpha: 1).setFill()
            NSBezierPath(roundedRect: NSRect(x: bounds.width - 4, y: y, width: 2, height: height), xRadius: 1, yRadius: 1).fill()
        }
    }
}
