import Foundation

enum PracticeTextError: LocalizedError {
    case blank, invalid, rightToLeft, control, tooLong

    var errorDescription: String? {
        switch self {
        case .blank: return "请先输入练习文本 / Enter some practice text first"
        case .invalid: return "文本含有不完整字符，请重新复制 / Copy the text again; it contains an invalid character"
        case .rightToLeft: return "暂不支持从右向左排版的文本 / Right-to-left scripts are not supported"
        case .control: return "文本中有无法输入的控制字符 / The text contains a control character that cannot be typed"
        case .tooLong: return "文本最多支持 40,000 个字符 / Use no more than 40,000 characters"
        }
    }
}

enum PracticeText {
    static func normalize(_ text: String, code: Bool) throws -> String {
        let canonical = text.precomposedStringWithCanonicalMapping
            .replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
        var result = ""
        var pendingSpace = false
        for scalar in canonical.unicodeScalars {
            let value = scalar.value
            if value == 0xFEFF || value == 0x200B { continue }
            if value == 0xFFFD || (value & 0xFFFF) == 0xFFFE || (value & 0xFFFF) == 0xFFFF {
                throw PracticeTextError.invalid
            }
            if isRightToLeft(value) { throw PracticeTextError.rightToLeft }
            if code && value == 10 { result.append("\n"); continue }
            if code && value == 9 { result.append("    "); continue }
            if !code && CharacterSet.whitespacesAndNewlines.contains(scalar) {
                pendingSpace = !result.isEmpty
                continue
            }
            if scalar.properties.generalCategory == .control { throw PracticeTextError.control }
            if pendingSpace { result.append(" ") }
            pendingSpace = false
            if value == 0xA0 { result.append(" ") }
            else { result.unicodeScalars.append(scalar) }
        }
        if code {
            result = result.trimmingCharacters(in: CharacterSet(charactersIn: "\n"))
        } else {
            result = result.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        guard !result.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw PracticeTextError.blank }
        guard result.count <= 40_000 else { throw PracticeTextError.tooLong }
        return result
    }

    static func elements(_ text: String) -> [String] { text.map { String($0) } }

    static func shorten(_ text: String, maximum: Int, code: Bool) throws -> String {
        let units = elements(try normalize(text, code: code))
        var end = max(1, maximum)
        if units.count <= end { return units.joined() }
        if code {
            var line = end
            while line > end / 2 && units[line - 1] != "\n" { line -= 1 }
            if line > end / 2 { end = line }
        } else {
            var word = end
            while word > end * 3 / 4 && isWord(units[word]) { word -= 1 }
            if word > end * 3 / 4 { end = word }
        }
        return units.prefix(end).joined().replacingOccurrences(of: "\\s+$", with: "", options: .regularExpression)
    }

    static func tokenMatches(_ token: String, language: String) -> Bool {
        guard !token.isEmpty else { return false }
        let chinese = token.unicodeScalars.contains { (0x3400...0x9FFF).contains($0.value) }
        let latin = token.unicodeScalars.contains { (65...90).contains($0.value) || (97...122).contains($0.value) }
        if language == "zh" { return chinese }
        if language == "en" { return latin && !chinese }
        return true
    }

    static func cellSpan(_ unit: String) -> Int {
        guard let c = unit.unicodeScalars.first?.value else { return 1 }
        let wide = c >= 0x1100 && (c <= 0x115F || c == 0x2329 || c == 0x232A
            || (0x2E80...0xA4CF).contains(c) || (0xAC00...0xD7A3).contains(c)
            || (0xF900...0xFAFF).contains(c) || (0xFE10...0xFE6F).contains(c)
            || (0xFF00...0xFF60).contains(c) || c >= 0x1F000)
        return wide ? 2 : 1
    }

    static func isWord(_ unit: String) -> Bool {
        guard cellSpan(unit) == 1, let scalar = unit.unicodeScalars.first else { return false }
        return CharacterSet.alphanumerics.contains(scalar) || unit == "_" || unit == "'" || unit == "-"
    }

    static func mistakeToken(_ units: [String], at index: Int) -> String {
        guard !units.isEmpty && units.indices.contains(index) else { return "" }
        var current = index
        if units[current] == " " || units[current] == "\n" { current = max(0, current - 1) }
        var start = current
        var end = current + 1
        if isWord(units[current]) {
            while start > 0 && isWord(units[start - 1]) { start -= 1 }
            while end < units.count && isWord(units[end]) { end += 1 }
        } else if cellSpan(units[current]) > 1 {
            start = max(0, current - 1)
            end = min(units.count, current + 2)
        }
        return units[start..<min(end, start + 32)].joined().trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func isRightToLeft(_ value: UInt32) -> Bool {
        return (0x0590...0x08FF).contains(value) || (0xFB1D...0xFDFF).contains(value)
            || (0xFE70...0xFEFC).contains(value) || (0x10800...0x10FFF).contains(value)
            || (0x1E800...0x1EEFF).contains(value) || value == 0x200F || value == 0x202B
            || value == 0x202E || value == 0x2067
    }
}

final class TypingSession {
    let target: String
    let targetUnits: [String]
    private(set) var inputUnits: [String] = []
    private(set) var attempts = 0
    private(set) var totalMistakes = 0
    private(set) var combo = 0
    private(set) var bestCombo = 0
    private(set) var score = 0
    private(set) var missedTokens: [String] = []
    private(set) var reason: FinishReason = .none

    private let options: SessionOptions
    private let now: () -> Double
    private var rewarded: Set<Int> = []
    private var elapsed: Double = 0
    private var last: Double = 0
    private var active = false
    private var correctAttempts = 0
    private(set) var hasStarted = false
    private(set) var isRunning = false

    init(target: String, options: SessionOptions = SessionOptions(), now: (() -> Double)? = nil) throws {
        self.options = options
        self.now = now ?? { ProcessInfo.processInfo.systemUptime }
        self.target = try PracticeText.normalize(target, code: options.isCode)
        targetUnits = PracticeText.elements(self.target)
    }

    var typed: String { inputUnits.joined() }
    var position: Int { inputUnits.count }
    var correctCount: Int { inputUnits.enumerated().filter { $0.element == targetUnits[$0.offset] }.count }
    var errorCount: Int { position - correctCount }
    var accuracy: Double { attempts == 0 ? 100 : Double(correctAttempts) * 100 / Double(attempts) }
    var isFinished: Bool { reason != .none }
    var elapsedSeconds: Double {
        let value = elapsed + (isRunning ? max(0, now() - last) : 0)
        return options.timeLimitSeconds > 0 ? min(value, Double(options.timeLimitSeconds)) : value
    }
    var charactersPerMinute: Double { Double(correctCount) * 60 / max(1, elapsedSeconds) }

    @discardableResult
    func commit(_ text: String) -> Bool {
        tick()
        guard !isFinished && !text.isEmpty else { return false }
        let text = text.replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n").precomposedStringWithCanonicalMapping
        var changed = false
        for unit in PracticeText.elements(text) {
            if isFinished { break }
            if unit == "\n" && !options.isCode { continue }
            if unit != "\n", let scalar = unit.unicodeScalars.first, scalar.properties.generalCategory == .control { continue }
            if !hasStarted {
                hasStarted = true
                runClock(active || options.countWhileUnfocused)
            }
            let index = position
            let correct = unit == targetUnits[index]
            inputUnits.append(unit)
            attempts += 1
            if correct {
                correctAttempts += 1
                if rewarded.insert(index).inserted {
                    combo += 1
                    bestCombo = max(bestCombo, combo)
                    score += options.game == .combo ? min(5, 1 + combo / 10) : 1
                }
            } else {
                totalMistakes += 1
                combo = 0
                let token = PracticeText.mistakeToken(targetUnits, at: index)
                if !token.isEmpty { missedTokens.append(token) }
            }
            changed = true
            if !correct && options.mistakeLimit > 0 && totalMistakes >= options.mistakeLimit {
                finish(.lost)
            } else if position >= targetUnits.count {
                let badCopy = [GameKind.memory, .race, .revenge].contains(options.game) && errorCount > 0
                let lowAccuracy = options.game == .combo && accuracy < 90
                finish(badCopy || lowAccuracy ? .lost : .completed)
            }
        }
        return changed
    }

    @discardableResult
    func backspace(word: Bool = false) -> Bool {
        tick()
        guard position > 0 else { return false }
        if isFinished {
            guard reason == .completed && options.game == .none && options.timeLimitSeconds <= 0 else { return false }
            reason = .none
        }
        var end = position - 1
        if word {
            end = position
            while end > 0 && inputUnits[end - 1].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { end -= 1 }
            if end > 0 && PracticeText.isWord(inputUnits[end - 1]) {
                while end > 0 && PracticeText.isWord(inputUnits[end - 1]) { end -= 1 }
            } else if end > 0 { end -= 1 }
        }
        inputUnits.removeSubrange(end..<position)
        runClock(active || options.countWhileUnfocused)
        return true
    }

    func setActive(_ value: Bool) {
        tick()
        active = value
        runClock(!isFinished && hasStarted && (active || options.countWhileUnfocused))
    }

    func tick() {
        if !isFinished && hasStarted && options.timeLimitSeconds > 0 && elapsedSeconds >= Double(options.timeLimitSeconds) {
            finish(options.game == .race ? .lost : .timeUp)
        }
    }

    private func runClock(_ run: Bool) {
        guard isRunning != run else { return }
        if isRunning { elapsed += max(0, now() - last) }
        isRunning = run
        if run { last = now() }
    }

    private func finish(_ result: FinishReason) {
        let at = elapsedSeconds
        runClock(false)
        elapsed = at
        reason = result
    }
}
