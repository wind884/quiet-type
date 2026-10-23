import AppKit

enum Ui {
    static let ink = NSColor(calibratedWhite: 0.14, alpha: 1)
    static let dim = NSColor(calibratedWhite: 0.48, alpha: 1)
    static let line = NSColor(calibratedWhite: 0.88, alpha: 1)
    static let accent = NSColor(calibratedRed: 0.36, green: 0.44, blue: 0.50, alpha: 1)
    static func pick(_ english: Bool, _ zh: String, _ en: String) -> String { english ? en : zh }
    static func page(_ value: Workspace, _ en: Bool) -> String {
        switch value {
        case .daily: return pick(en, "日常练习", "Daily practice")
        case .exams: return pick(en, "英语考试", "Exam writing")
        case .code: return pick(en, "程序员", "Code")
        case .games: return pick(en, "趣味挑战", "Challenges")
        case .test: return pick(en, "计时测试", "Timed test")
        case .library: return pick(en, "我的文本", "My texts")
        }
    }
    static func game(_ value: GameKind, _ en: Bool) -> String {
        switch value {
        case .none: return ""
        case .combo: return pick(en, "连击闯关", "Combo run")
        case .suddenDeath: return pick(en, "一命通关", "One life")
        case .memory: return pick(en, "记忆闪打", "Memory flash")
        case .race: return pick(en, "影子赛跑", "Ghost race")
        case .revenge: return pick(en, "错字复仇", "Mistake rematch")
        }
    }
    static func title(_ lesson: Lesson, _ en: Bool) -> String {
        en && !lesson.titleEn.isEmpty ? lesson.titleEn : lesson.title
    }
    static func language(_ raw: String, _ en: Bool) -> String {
        switch raw {
        case "en", "英语", "English": return pick(en, "英语", "English")
        case "zh", "中文", "Chinese": return pick(en, "中文", "Chinese")
        case "ja", "日本語": return pick(en, "日语", "Japanese")
        case "ko", "한국어": return pick(en, "韩语", "Korean")
        case "es", "Español": return pick(en, "西班牙语", "Spanish")
        case "fr", "Français": return pick(en, "法语", "French")
        case "de", "Deutsch": return pick(en, "德语", "German")
        case "ru", "Русский": return pick(en, "俄语", "Russian")
        case "mixed", "混合": return pick(en, "混合文本", "Mixed text")
        default: return raw
        }
    }
    static func record(_ raw: String, _ en: Bool) -> String {
        let pairs = ["中途结束":"Stopped early", "日常练习":"Daily practice", "英语考试":"Exam writing", "程序员":"Code", "趣味挑战":"Challenges", "计时测试":"Timed test", "我的文本":"My texts", "短篇精练":"Short practice", "短篇速练":"Short practice", "长篇精练":"Long practice", "练习完成":"Practice complete", "挑战完成":"Challenge complete", "测试结束":"Test finished", "挑战失败":"Challenge lost", "错字未改":"Uncorrected errors", "影子先到了":"The ghost finished first", "记忆有误":"Recall errors", "准确率未达90%":"Accuracy below 90%", "错字未清完":"Mistakes remain", "练习中":"In progress"]
        if raw.contains(" / ") { return raw.components(separatedBy: " / ").map { record($0, en) }.joined(separator: " / ") }
        return en ? (pairs[raw] ?? raw) : (pairs.first(where: { $0.value == raw })?.key ?? raw)
    }
    static func label(_ value: String = "", size: CGFloat = 12, bold: Bool = false, color: NSColor = Ui.ink) -> NSTextField {
        let label = NSTextField(labelWithString: value)
        label.font = .systemFont(ofSize: size, weight: bold ? .semibold : .regular)
        label.textColor = color
        label.lineBreakMode = .byTruncatingTail
        return label
    }
    static func button(_ title: String, target: AnyObject, action: Selector) -> NSButton {
        let value = NSButton(title: title, target: target, action: action)
        value.bezelStyle = .rounded
        value.font = .systemFont(ofSize: 12)
        return value
    }
    static func scroll(_ view: NSView) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.documentView = view
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        return scroll
    }
    static func time(_ seconds: Double) -> String {
        let whole = max(0, Int(seconds))
        return String(format: "%02d:%02d", whole / 60, whole % 60)
    }
    static func error(_ text: String, _ en: Bool) -> String {
        let parts = text.components(separatedBy: " / ")
        if parts.count > 1 { return en ? parts.dropFirst().joined(separator: " / ") : parts[0] }
        if !en || text.isEmpty { return text }
        if text.unicodeScalars.contains(where: { (0x3400...0x9fff).contains($0.value) }) {
            return "The text or local records could not be used. Check the content and folder access."
        }
        return text
    }
}

class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

final class CanvasView: FlippedView {
    var onLayout: (() -> Void)?
    var background: NSColor = .white
    override func layout() { super.layout(); onLayout?() }
    override func draw(_ dirtyRect: NSRect) { background.setFill(); dirtyRect.fill() }
}

final class RuleView: NSView {
    override func draw(_ dirtyRect: NSRect) { Ui.line.setFill(); bounds.fill() }
}

final class LessonCell: NSTableCellView {
    let titleLabel = Ui.label(size: 12)
    let detailLabel = Ui.label(size: 10, color: Ui.dim)
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        addSubview(titleLabel); addSubview(detailLabel)
    }
    required init?(coder: NSCoder) { return nil }
    override func layout() {
        super.layout()
        titleLabel.frame = NSRect(x: 10, y: 29, width: bounds.width - 20, height: 20)
        detailLabel.frame = NSRect(x: 10, y: 9, width: bounds.width - 20, height: 16)
    }
}
