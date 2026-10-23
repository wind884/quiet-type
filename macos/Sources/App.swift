import AppKit
import Darwin

@main
enum QuietTypeApp {
    static func main() {
        let app = NSApplication.shared
        let automated = CommandLine.arguments.contains("--smoke-test") || CommandLine.arguments.contains("--screenshot")
        let directory = automated ? FileManager.default.temporaryDirectory.appendingPathComponent("QuietType-\(UUID().uuidString)") : nil
        let delegate = AppDelegate(store: UserStore(directory: directory))
        app.setActivationPolicy(.regular)
        app.delegate = delegate
        app.run()
        withExtendedLifetime(delegate) {}
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSTableViewDataSource, NSTableViewDelegate, NSTextFieldDelegate {
    private let store: UserStore
    private var catalog: [Lesson] = []
    private var displayed: [Lesson] = []
    private var languageChoices: [Choice] = []
    private var groupChoices: [Choice] = []
    private let pages: [Workspace] = [.daily, .exams, .code, .games, .test, .library]
    private var page: Workspace = .daily
    private var roundPage: Workspace = .daily
    private var current: Lesson?
    private var english = false
    private var loading = false
    private var roundLong = false
    private var saved = false
    private var roundID = ""
    private var recordedMistakes = 0
    private var revengeUsed: [String] = []
    private var memoryWaiting = false
    private var memoryPreview = false
    private var peeking = false
    private var memoryStarted = 0.0
    private var peeksLeft = 2
    private var peeksUsed = 0
    private var roundTimeLimit = 0
    private var timer: Timer?
    private var catalogError = ""
    private var mainWindow: NSWindow!
    private let root = CanvasView()
    private let sidebar = CanvasView()
    private let middle = CanvasView()
    private let leftRule = RuleView()
    private let rightRule = RuleView()
    private let brand = Ui.label(size: 18, bold: true)
    private let subtitle = Ui.label(size: 10, color: Ui.dim)
    private let credit = Ui.label("GraysonC", size: 9, color: NSColor(calibratedWhite: 0.72, alpha: 1))
    private var nav: [NSButton] = []
    private let middleTitle = Ui.label(size: 16, bold: true)
    private let firstLabel = Ui.label(size: 10, color: Ui.dim)
    private let secondLabel = Ui.label(size: 10, color: Ui.dim)
    private let searchLabel = Ui.label(size: 10, color: Ui.dim)
    private let textCount = Ui.label(size: 11, color: Ui.dim)
    private let language = NSPopUpButton(frame: .zero, pullsDown: false)
    private let group = NSPopUpButton(frame: .zero, pullsDown: false)
    private let search = NSSearchField()
    private let lessons = NSTableView()
    private var lessonScroll: NSScrollView!
    private let titleLabel = Ui.label(size: 21, bold: true)
    private let descriptionLabel = Ui.label(size: 11, color: Ui.dim)
    private let length = NSSegmentedControl(labels: ["", ""], trackingMode: .selectOne, target: nil, action: nil)
    private let fontLabel = Ui.label(size: 11, color: Ui.dim)
    private let fontSize = NSPopUpButton(frame: .zero, pullsDown: false)
    private let duration = NSPopUpButton(frame: .zero, pullsDown: false)
    private let pace = NSPopUpButton(frame: .zero, pullsDown: false)
    private let statLabels = (0..<4).map { _ in Ui.label(size: 10, color: Ui.dim) }
    private let statValues = (0..<4).map { _ in Ui.label(size: 21) }
    private let modeLabel = Ui.label(size: 10, color: Ui.dim)
    private let footer = Ui.label(size: 10, color: Ui.dim)
    private let paper = CanvasView()
    let practice = PracticeView(frame: .zero)
    private let progress = NSProgressIndicator()
    private let race = RaceView()
    private var nextButton: NSButton!
    private var shuffleButton: NSButton!
    private var restartButton: NSButton!
    private var peekButton: NSButton!
    private var historyButton: NSButton!
    private var localeButton: NSButton!
    private var libraryButton: NSButton!
    private let durationValues = [30, 60, 120, 180, 300]
    private let paceValues = [90, 120, 180, 240]
    private var longMode: Bool { length.selectedSegment == 1 }
    private func t(_ zh: String, _ en: String) -> String { Ui.pick(english, zh, en) }
    private var languageKey: String { languageChoices.indices.contains(language.indexOfSelectedItem) ? languageChoices[language.indexOfSelectedItem].key : "" }
    private var groupKey: String { groupChoices.indices.contains(group.indexOfSelectedItem) ? groupChoices[group.indexOfSelectedItem].key : "" }

    init(store: UserStore) { self.store = store; super.init() }

    func applicationDidFinishLaunching(_ notification: Notification) {
        store.load(); english = store.data.uiLanguage == "en"
        do {
            guard let url = Bundle.main.url(forResource: "lessons", withExtension: "json") else {
                throw NSError(domain: "QuietType", code: 1, userInfo: [NSLocalizedDescriptionKey: "The practice catalogue is missing from this app."])
            }
            catalog = try Catalog.load(url: url)
        } catch { catalogError = error.localizedDescription }
        buildWindow()
        selectWorkspace(.daily)
        mainWindow.center(); mainWindow.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        practice.focusInput()
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in self?.tick() }
        if CommandLine.arguments.contains("--smoke-test") || CommandLine.arguments.contains("--screenshot") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { [weak self] in self?.runAutomation() }
        }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ notification: Notification) {
        saveRound("中途结束")
        store.data.uiLanguage = english ? "en" : "zh"; store.data.fontSize = practice.textSize
        store.save(); timer?.invalidate()
    }
    func windowDidBecomeKey(_ notification: Notification) { practice.updateActivity() }
    func windowDidResignKey(_ notification: Notification) { practice.session?.setActive(false) }
    private func buildWindow() {
        mainWindow = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1230, height: 760), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        mainWindow.minSize = NSSize(width: 1060, height: 660)
        mainWindow.delegate = self
        mainWindow.isReleasedWhenClosed = false
        mainWindow.backgroundColor = .white
        mainWindow.appearance = NSAppearance(named: .aqua)
        mainWindow.contentView = root
        root.onLayout = { [weak self] in self?.layout() }
        sidebar.background = NSColor(calibratedWhite: 0.965, alpha: 1)
        for view in [sidebar, middle, leftRule, rightRule] as [NSView] { root.addSubview(view) }
        for view in [brand, subtitle, credit] { sidebar.addSubview(view) }
        for (index, page) in pages.enumerated() {
            let button = NSButton(title: Ui.page(page, english), target: self, action: #selector(navigate(_:)))
            button.tag = index; button.isBordered = false; button.alignment = .left
            button.font = .systemFont(ofSize: 12)
            button.wantsLayer = true; nav.append(button); sidebar.addSubview(button)
        }
        historyButton = Ui.button("", target: self, action: #selector(openHistory))
        localeButton = Ui.button("", target: self, action: #selector(toggleLocale))
        libraryButton = Ui.button("", target: self, action: #selector(openLibrary))
        sidebar.addSubview(historyButton); sidebar.addSubview(localeButton)
        for view in [middleTitle, firstLabel, secondLabel, language, group, searchLabel, search, textCount, libraryButton] as [NSView] { middle.addSubview(view) }
        language.target = self; language.action = #selector(languageChanged)
        group.target = self; group.action = #selector(groupChanged)
        for popup in [language, group, fontSize, duration, pace] { popup.font = .systemFont(ofSize: 12) }
        search.delegate = self; search.sendsSearchStringImmediately = true
        search.target = self; search.action = #selector(searchChanged)
        lessons.addTableColumn(NSTableColumn(identifier: NSUserInterfaceItemIdentifier("lesson")))
        lessons.headerView = nil; lessons.rowHeight = 61; lessons.intercellSpacing = .zero
        lessons.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
        lessons.dataSource = self; lessons.delegate = self
        lessons.selectionHighlightStyle = .regular
        lessons.setAccessibilityLabel(t("文章列表", "Practice texts"))
        lessonScroll = Ui.scroll(lessons); middle.addSubview(lessonScroll)
        for view in [titleLabel, descriptionLabel, length, fontLabel, fontSize, duration, pace, modeLabel, race, paper, progress, footer] as [NSView] { root.addSubview(view) }
        descriptionLabel.maximumNumberOfLines = 2; descriptionLabel.lineBreakMode = .byWordWrapping
        length.selectedSegment = 0; length.target = self; length.action = #selector(lengthChanged)
        for size in 13...21 { fontSize.addItem(withTitle: "\(size)") }
        practice.textSize = store.data.fontSize
        fontSize.selectItem(at: practice.textSize - 13); fontSize.target = self; fontSize.action = #selector(fontChanged)
        duration.target = self; duration.action = #selector(durationChanged)
        pace.target = self; pace.action = #selector(paceChanged)
        paper.wantsLayer = true; paper.layer?.borderWidth = 1; paper.layer?.borderColor = Ui.line.cgColor
        paper.addSubview(practice)
        progress.isIndeterminate = false; progress.style = .bar; progress.minValue = 0; progress.maxValue = 1
        for view in statLabels + statValues { root.addSubview(view) }
        nextButton = Ui.button("", target: self, action: #selector(nextText))
        shuffleButton = Ui.button("", target: self, action: #selector(shuffleText))
        restartButton = Ui.button("", target: self, action: #selector(restart))
        peekButton = Ui.button("", target: self, action: #selector(peek))
        for view in [nextButton, shuffleButton, restartButton, peekButton] { root.addSubview(view!) }
        practice.onChange = { [weak self] in
            guard let self = self, !self.loading else { return }
            if self.practice.session?.isFinished == false { self.saved = false }
            self.handleFinished(); self.updateStatus()
        }
        applyLocale(); duration.selectItem(at: 1)
    }

    private func applyLocale() {
        let previousLoading = loading
        loading = true
        defer { loading = previousLoading }
        mainWindow.title = t("轻打", "Quiet Type")
        brand.stringValue = t("轻打", "Quiet Type"); subtitle.stringValue = t("离线打字练习", "Offline typing")
        for (index, value) in pages.enumerated() {
            nav[index].title = "   " + Ui.page(value, english)
            let active = value == page
            nav[index].font = .systemFont(ofSize: 12, weight: active ? .semibold : .regular)
            nav[index].contentTintColor = active ? Ui.ink : Ui.dim
            nav[index].layer?.backgroundColor = (active ? NSColor(calibratedRed: 0.90, green: 0.92, blue: 0.93, alpha: 1) : .clear).cgColor
        }
        middleTitle.stringValue = Ui.page(page, english)
        length.setLabel(t("短篇速练", "Short practice"), forSegment: 0)
        length.setLabel(t("长篇精练", "Long practice"), forSegment: 1)
        fontLabel.stringValue = t("字号", "Text size")
        let d = max(0, duration.indexOfSelectedItem), p = max(0, pace.indexOfSelectedItem)
        duration.removeAllItems(); duration.addItems(withTitles: durationValues.map { "\($0)" + t(" 秒", " sec") }); duration.selectItem(at: min(d, durationValues.count - 1))
        pace.removeAllItems(); pace.addItems(withTitles: paceValues.map { "\($0)" + t(" 字/分", " chars/min") }); pace.selectItem(at: min(p, paceValues.count - 1))
        nextButton.title = t("下一篇", "Next text"); shuffleButton.title = t("随机", "Shuffle")
        restartButton.title = memoryWaiting ? t("开始记忆", "Start recall") : t("重来", "Restart")
        peekButton.title = t("偷看 ", "Peek ") + "\(peeksLeft)"
        historyButton.title = t("练习记录", "History"); localeButton.title = english ? "中文" : "English"
        libraryButton.title = t("管理文本", "Manage texts")
        let modeFirst = page == .games || page == .exams
        firstLabel.stringValue = modeFirst ? (page == .games ? t("玩法", "Challenge") : t("题型", "Task type")) : t("练习语言", "Text language")
        secondLabel.stringValue = modeFirst ? t("练习语言", "Text language") : t("文章分类", "Topic")
        searchLabel.stringValue = t("搜索文章标题", "Search titles")
        search.placeholderString = t("搜索标题", "Search titles")
        language.setAccessibilityLabel(t("练习语言", "Text language"))
        group.setAccessibilityLabel(modeFirst ? firstLabel.stringValue : secondLabel.stringValue)
        practice.setAccessibilityLabel(t("打字练习区", "Typing area"))
        localeButton.toolTip = t("切换界面语言，保留当前练习", "Switch interface language and keep this round")
        restartButton.toolTip = t("重开当前练习  ⌘R", "Restart this round  ⌘R")
        nextButton.toolTip = t("下一篇  ⌘N", "Next text  ⌘N")
        titleLabel.stringValue = current.map { Ui.title($0, english) } ?? (page == .library ? t("练你自己的文字", "Your words, your practice") : t("没有找到文章", "No matching texts"))
        descriptionLabel.stringValue = current.map { t($0.description, $0.descriptionEn) } ?? (page == .library ? t("管理文本，粘贴文章或导入 TXT", "Use Manage texts to paste an article or import TXT") : t("换一个分类，或清空搜索试试", "Try another topic or clear the search"))
        lessons.reloadData(); updateStatus(); buildMenu(); root.needsLayout = true
    }
    private func buildMenu() {
        let bar = NSMenu()
        func submenu(_ title: String) -> NSMenu { let item = NSMenuItem(title: title, action: nil, keyEquivalent: ""); let menu = NSMenu(title: title); item.submenu = menu; bar.addItem(item); return menu }
        let app = submenu("Quiet Type")
        app.addItem(withTitle: t("退出轻打", "Quit Quiet Type"), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let file = submenu(t("文件", "File"))
        file.addItem(withTitle: t("管理文本…", "Manage texts…"), action: #selector(openLibrary), keyEquivalent: "").target = self
        file.addItem(withTitle: t("练习记录…", "Practice history…"), action: #selector(openHistory), keyEquivalent: "").target = self
        let edit = submenu(t("编辑", "Edit"))
        edit.addItem(withTitle: t("撤销", "Undo"), action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: t("剪切", "Cut"), action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: t("复制", "Copy"), action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: t("粘贴", "Paste"), action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: t("全选", "Select All"), action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        let training = submenu(t("练习", "Practice"))
        training.addItem(withTitle: t("重来", "Restart"), action: #selector(restart), keyEquivalent: "r").target = self
        training.addItem(withTitle: t("下一篇", "Next text"), action: #selector(nextText), keyEquivalent: "n").target = self
        let random = training.addItem(withTitle: t("随机", "Shuffle"), action: #selector(shuffleText), keyEquivalent: "N"); random.target = self
        NSApp.mainMenu = bar
    }
    private func populateLanguages(preferred: String) {
        languageChoices = Catalog.languages(catalog, page: page, saved: store.data.texts)
        language.removeAllItems(); language.addItems(withTitles: languageChoices.map { english ? $0.en : $0.zh })
        if !languageChoices.isEmpty { language.selectItem(at: languageChoices.firstIndex(where: { $0.key == preferred }) ?? 0) }
    }
    private func populateGroups(preferred: String) {
        groupChoices = Catalog.groups(page: page)
        if page == .daily || page == .test {
            groupChoices = groupChoices.filter { $0.key.isEmpty || !Catalog.query(catalog, page: page, language: languageKey, group: $0.key, search: "", saved: store.data.texts).isEmpty }
        }
        group.removeAllItems(); group.addItems(withTitles: groupChoices.map { english ? $0.en : $0.zh })
        if !groupChoices.isEmpty { group.selectItem(at: groupChoices.firstIndex(where: { $0.key == preferred }) ?? 0) }
    }
    private func selectWorkspace(_ value: Workspace, preferredID: String = "") {
        saveRound("中途结束"); loading = true; page = value; search.stringValue = ""
        let selected = Catalog.query(catalog, page: value, language: "", group: "", search: "", saved: store.data.texts).first(where: { $0.id == preferredID })
        populateLanguages(preferred: selected.map { Catalog.languageKey($0) } ?? "en")
        populateGroups(preferred: ""); loading = false
        refreshLessons(preferredID: preferredID, force: true)
    }
    private func refreshLessons(preferredID: String = "", force: Bool = false) {
        let preferred = preferredID.isEmpty ? current?.id ?? "" : preferredID
        displayed = Catalog.query(catalog, page: page, language: languageKey, group: page == .code || page == .library ? "" : groupKey, search: search.stringValue, saved: store.data.texts)
        loading = true; lessons.reloadData()
        let selected = displayed.firstIndex(where: { $0.id == preferred }) ?? 0
        if !displayed.isEmpty { lessons.selectRowIndexes(IndexSet(integer: selected), byExtendingSelection: false); lessons.scrollRowToVisible(selected) } else { lessons.deselectAll(nil) }
        loading = false
        let candidate = displayed.indices.contains(lessons.selectedRow) ? displayed[lessons.selectedRow] : nil
        if force || candidate?.id != current?.id || candidate?.longText != current?.longText { startCurrent(false) } else { applyLocale() }
    }
    private func targetFor(_ value: Lesson) throws -> String {
        revengeUsed = []
        var target = longMode ? value.longText : value.shortText
        if value.game == .revenge {
            revengeUsed = Array(store.data.mistakes.filter { PracticeText.tokenMatches($0.text, language: Catalog.languageKey(value)) }.prefix(longMode ? 35 : 10)).map { $0.text }
            if !revengeUsed.isEmpty { target = revengeUsed.joined(separator: " "); if longMode { target = [target, target, target].joined(separator: " ") } }
        }
        if page == .test {
            let seed = target
            if !seed.isEmpty { while target.count < (longMode ? 12000 : 4000) { target += " " + seed } }
            if PracticeText.elements(target).count > 35000 { target = try PracticeText.shorten(target, maximum: 35000, code: false) }
        }
        return try PracticeText.normalize(target, code: value.isCode)
    }
    private func startCurrent(_ startButton: Bool) {
        guard !loading else { return }
        saveRound("中途结束")
        let previous = current
        current = displayed.indices.contains(lessons.selectedRow) ? displayed[lessons.selectedRow] : nil
        guard let value = current else { memoryWaiting = false; memoryPreview = false; peeking = false; practice.clear(); applyLocale(); return }
        loading = true
        do {
            var options = SessionOptions()
            options.game = value.game; options.isCode = value.isCode; options.stageCount = longMode ? 7 : 3
            if value.game == .race && (previous?.game != .race || previous.map { Catalog.languageKey($0) } != Catalog.languageKey(value)) { pace.selectItem(at: Catalog.languageKey(value) == "zh" ? 1 : 2) }
            if page == .test { options.timeLimitSeconds = durationValues[max(0, duration.indexOfSelectedItem)]; options.countWhileUnfocused = true }
            if value.game == .suddenDeath { options.mistakeLimit = 1 }
            if value.game == .revenge { options.mistakeLimit = longMode ? 5 : 3 }
            let target = try targetFor(value)
            if value.game == .race { options.timeLimitSeconds = max(1, Int(ceil(Double(PracticeText.elements(target).count) * 60 / Double(paceValues[max(0, pace.indexOfSelectedItem)])))); options.countWhileUnfocused = true }
            roundTimeLimit = options.timeLimitSeconds
            let session = try TypingSession(target: target, options: options)
            roundID = UUID().uuidString; recordedMistakes = 0; saved = false; roundLong = longMode; roundPage = page
            memoryWaiting = value.game == .memory; memoryPreview = false; peeking = false; peeksLeft = longMode ? 3 : 2; peeksUsed = 0
            practice.inputAllowed = !memoryWaiting; practice.load(session, isCode: value.isCode); practice.maskReference = memoryWaiting
            loading = false
            if memoryWaiting && startButton { beginMemory() }
            else { practice.focusInput() }
        } catch {
            loading = false; catalogError = error.localizedDescription; current = nil
        }
        applyLocale()
    }
    private func beginMemory() {
        memoryWaiting = false; memoryPreview = true; peeking = false; memoryStarted = ProcessInfo.processInfo.systemUptime
        practice.inputAllowed = false; practice.maskReference = false
        restartButton.title = t("重来", "Restart"); peekButton.isEnabled = false
        updateStatus()
    }
    private func tick() {
        guard let value = current, let session = practice.session else { return }
        let elapsed = ProcessInfo.processInfo.systemUptime - memoryStarted
        if memoryPreview && elapsed >= (roundLong ? 8 : 4) {
            memoryPreview = false; practice.maskReference = true; practice.inputAllowed = true; peekButton.isEnabled = true; practice.focusInput()
        }
        if peeking && elapsed >= 1.8 { peeking = false; practice.maskReference = true; peekButton.isEnabled = peeksLeft > 0 }
        session.tick()
        if value.game == .race { practice.ghostPosition = min(session.targetUnits.count - 1, Int(session.elapsedSeconds * Double(paceValues[max(0, pace.indexOfSelectedItem)]) / 60)) }
        handleFinished(); updateStatus()
    }
    private var finalScore: Int { (practice.session?.score ?? 0) * (current?.game == .memory ? max(1, 3 - peeksUsed) : 1) }
    private func handleFinished() {
        guard let session = practice.session, session.isFinished, !saved, let value = current else { return }
        saveRound("")
        if value.game == .revenge && session.reason == .completed && session.errorCount == 0 && !revengeUsed.isEmpty { store.clearMistakes(revengeUsed) }
        practice.maskReference = false; peekButton.isEnabled = false
    }
    private func resultTitle() -> String {
        guard let s = practice.session else { return "练习中" }
        if s.reason == .timeUp { return "测试结束" }
        if s.reason == .completed { return roundPage == .games ? "挑战完成" : "练习完成" }
        if s.reason == .lost {
            switch current?.game {
            case .race?: return s.position >= s.targetUnits.count ? "错字未改" : "影子先到了"
            case .memory?: return "记忆有误"
            case .combo?: return "准确率未达90%"
            case .revenge?: return "错字未清完"
            default: return "挑战失败"
            }
        }
        return "练习中"
    }
    private func saveRound(_ interrupted: String) {
        guard let s = practice.session, let value = current, s.hasStarted, !saved else { return }
        var record = ScoreRecord()
        record.id = roundID; record.whenIso = ISO8601DateFormatter().string(from: Date()); record.section = Ui.page(roundPage, false)
        record.title = (value.game == .none ? "" : Ui.game(value.game, false) + " / ") + value.title
        record.titleEn = (value.game == .none ? "" : Ui.game(value.game, true) + " / ") + Ui.title(value, true)
        record.language = value.language; record.lengthMode = roundLong ? "长篇精练" : "短篇速练"
        record.result = s.isFinished ? resultTitle() : interrupted; record.cpm = s.charactersPerMinute.rounded(); record.accuracy = (s.accuracy * 10).rounded() / 10
        record.seconds = s.elapsedSeconds; record.score = finalScore; record.bestCombo = s.bestCombo; record.typedCount = s.position; record.correctCount = s.correctCount
        store.record(record, missedTokens: Array(s.missedTokens.dropFirst(recordedMistakes)))
        recordedMistakes = s.missedTokens.count; saved = true
    }

    @objc private func navigate(_ sender: NSButton) { selectWorkspace(pages[sender.tag]) }
    @objc private func languageChanged() { guard !loading else { return }; let old = groupKey; populateGroups(preferred: old); refreshLessons() }
    @objc private func groupChanged() { if !loading { refreshLessons() } }
    @objc private func searchChanged() { if !loading { refreshLessons() } }
    func controlTextDidChange(_ obj: Notification) { if obj.object as? NSSearchField === search { searchChanged() } }
    @objc private func lengthChanged() { guard !loading else { return }; duration.selectItem(at: longMode ? 3 : 1); startCurrent(false) }
    @objc private func durationChanged() { if !loading && page == .test { startCurrent(false) } }
    @objc private func paceChanged() { if !loading && current?.game == .race { startCurrent(false) } }
    @objc private func fontChanged() { practice.textSize = fontSize.indexOfSelectedItem + 13; store.data.fontSize = practice.textSize; practice.focusInput() }
    @objc private func restart() { startCurrent(true) }
    @objc private func nextText() {
        guard !displayed.isEmpty else { return }
        let row = (max(0, lessons.selectedRow) + 1) % displayed.count
        if row == lessons.selectedRow { startCurrent(false) } else { lessons.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false); lessons.scrollRowToVisible(row) }
    }
    @objc private func shuffleText() {
        guard displayed.count > 1 else { return }
        var row = Int.random(in: 0..<(displayed.count - 1)); if row >= lessons.selectedRow { row += 1 }
        lessons.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false); lessons.scrollRowToVisible(row)
    }
    @objc private func toggleLocale() {
        english.toggle(); loading = true
        let lang = languageKey, selectedGroup = groupKey
        populateLanguages(preferred: lang); populateGroups(preferred: selectedGroup)
        applyLocale(); loading = false
        store.data.uiLanguage = english ? "en" : "zh"; store.save(); practice.focusInput()
    }
    @objc private func peek() {
        guard current?.game == .memory, !memoryWaiting, !memoryPreview, !peeking, practice.session?.isFinished == false, peeksLeft > 0 else { return }
        peeksLeft -= 1; peeksUsed += 1; peeking = true; practice.maskReference = false; memoryStarted = ProcessInfo.processInfo.systemUptime
        peekButton.title = t("偷看 ", "Peek ") + "\(peeksLeft)"; peekButton.isEnabled = false; practice.focusInput(); updateStatus()
    }
    @objc private func openLibrary() {
        let dialog = LibraryWindow(store: store, english: english)
        let selected = dialog.run(parent: mainWindow)
        if let id = selected { selectWorkspace(.library, preferredID: "user:" + id) }
        else if page == .library { populateLanguages(preferred: languageKey); refreshLessons(force: true) }
        practice.focusInput(); updateStatus()
    }
    @objc private func openHistory() { let dialog = HistoryWindow(store: store, english: english); dialog.run(parent: mainWindow); practice.focusInput() }

    func numberOfRows(in tableView: NSTableView) -> Int { displayed.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard displayed.indices.contains(row) else { return nil }
        let lesson = displayed[row], cell = LessonCell()
        cell.titleLabel.stringValue = Ui.title(lesson, english)
        let topic = page == .games ? Ui.game(lesson.game, english) : lesson.isCode ? lesson.language : t(lesson.category, lesson.categoryEn)
        cell.detailLabel.stringValue = topic + t("  ·  全文 ", "  ·  Full ") + "\(PracticeText.elements(lesson.longText).count)" + t(" 字符", " chars")
        return cell
    }
    func tableViewSelectionDidChange(_ notification: Notification) { if !loading { startCurrent(false) } }

    private func updateStatus() {
        textCount.stringValue = "\(displayed.count)" + t(" 篇可选", " texts")
        guard let s = practice.session, let value = current else {
            footer.stringValue = Ui.error(catalogError.isEmpty ? store.lastError : catalogError, english)
            return
        }
        let speed = !s.hasStarted || s.elapsedSeconds < 2 ? "—" : "\(Int(s.charactersPerMinute.rounded()))"
        let timed = page == .test || value.game == .race
        let names = [t("速度 · 字/分", "Speed · chars/min"), t("准确率", "Accuracy"), timed ? t("剩余时间", "Time left") : t("用时", "Time"), t("进度", "Progress")]
        let values = [speed, s.attempts == 0 ? "—" : String(format: "%.1f%%", s.accuracy), Ui.time(timed ? ceil(max(0, Double(roundTimeLimit) - s.elapsedSeconds)) : floor(s.elapsedSeconds)), "\(s.position) / \(s.targetUnits.count)"]
        for index in 0..<4 { statLabels[index].stringValue = names[index]; statValues[index].stringValue = values[index] }
        progress.doubleValue = Double(s.position) / Double(max(1, s.targetUnits.count))
        peekButton.isEnabled = value.game == .memory && !memoryWaiting && !memoryPreview && !peeking && !s.isFinished && peeksLeft > 0
        if page == .test { modeLabel.stringValue = t("首次输入开始计时    切换窗口计时继续", "The first key starts the clock    Time keeps running outside this window") }
        else {
            switch value.game {
            case .combo:
                let stages = roundLong ? 7 : 3
                modeLabel.stringValue = t("第 ", "Stage ") + "\(min(stages, s.position * stages / max(1, s.targetUnits.count) + 1)) / \(stages)    \(s.combo)" + t(" 连击", " combo") + "    ×\(min(5, 1 + s.combo / 10))    " + t("得分 ", "Score ") + "\(s.score)" + t("    90%准确率过关", "    Finish with 90% accuracy")
            case .suddenDeath: modeLabel.stringValue = t("错一个字就结束    已走过 ", "One wrong key ends the run    Correct ") + "\(s.correctCount)" + t(" 字", " chars")
            case .memory:
                if memoryWaiting { modeLabel.stringValue = t("按「开始记忆」，先看原文，再藏起盲打", "Press Start recall to preview, then type from memory") }
                else if memoryPreview { modeLabel.stringValue = t("记住这一段    还剩 ", "Remember this    ") + "\(max(1, Int(ceil((roundLong ? 8.0 : 4.0) - (ProcessInfo.processInfo.systemUptime - memoryStarted)))))" + t(" 秒", " sec left") }
                else { modeLabel.stringValue = (peeking ? t("偷看中", "Peeking") : t("原文已藏起", "Text hidden")) + t("    剩余偷看 ", "    Peeks left ") + "\(peeksLeft)" + t("    完成得分 ×", "    Finish score ×") + "\(max(1, 3 - peeksUsed))" }
            case .revenge: modeLabel.stringValue = (revengeUsed.isEmpty ? t("易错词热身", "Warm-up words") : t("复仇清单 ", "Mistake list ") + "\(revengeUsed.count)") + t("    剩余 ", "    Lives ") + "\(max(0, (roundLong ? 5 : 3) - s.totalMistakes))" + t("    全部改对清掉清单", "    Correct every word to clear the list")
            case .race:
                race.english = english; race.actual = Double(s.correctCount) / Double(max(1, s.targetUnits.count)); race.ghost = min(1, s.elapsedSeconds * Double(paceValues[max(0, pace.indexOfSelectedItem)]) / 60 / Double(max(1, s.targetUnits.count)))
                let lead = s.correctCount - Int(race.ghost * Double(s.targetUnits.count))
                race.caption = (lead >= 0 ? t("领先 ", "Ahead ") : t("落后 ", "Behind ")) + "\(abs(lead))"; race.needsDisplay = true
            case .none: modeLabel.stringValue = t(value.sourceInfo, value.sourceInfoEn)
            }
        }
        footer.textColor = Ui.dim
        if !store.lastError.isEmpty { footer.stringValue = Ui.error(store.lastError, english) }
        else if s.isFinished {
            footer.stringValue = Ui.record(resultTitle(), english) + "   \(Int(s.charactersPerMinute.rounded()))" + t(" 字/分   准确率 ", " chars/min   Accuracy ") + String(format: "%.1f%%", s.accuracy) + (page == .games ? t("   得分 ", "   Score ") + "\(finalScore)" + t("   最高连击 ", "   Best combo ") + "\(s.bestCombo)" : "") + "   \(s.errorCount)" + t(" 处未改错误", " uncorrected")
            footer.textColor = s.reason == .lost ? NSColor(calibratedRed: 0.63, green: 0.31, blue: 0.31, alpha: 1) : Ui.ink
        } else if value.isCode { footer.stringValue = t("Enter 换行    Tab 缩进    Backspace 修改    ⌘R 重来", "Enter new line    Tab indent    Backspace edit    ⌘R restart") }
        else if !s.hasStarted { footer.stringValue = t("点击白色区域开始    中文和其他语言使用系统输入法", "Click the white area to begin    Use your system input method") }
        else if !s.isRunning && page != .test && value.game != .race { footer.stringValue = t("已暂停    点击文字区域继续", "Paused    Click the typing area to continue") }
        else { footer.stringValue = t("Backspace 修改    ⌥⌫ / ⌘⌫ 删词    ⌘R 重来", "Backspace edit    ⌥⌫ / ⌘⌫ delete word    ⌘R restart") }
    }

    private func layout() {
        let width = root.bounds.width, height = root.bounds.height
        let side: CGFloat = 146, list: CGFloat = 252, x = side + list + 26, w = max(250, width - x - 26)
        sidebar.frame = NSRect(x: 0, y: 0, width: side, height: height)
        middle.frame = NSRect(x: side, y: 0, width: list, height: height)
        leftRule.frame = NSRect(x: side, y: 0, width: 1, height: height); rightRule.frame = NSRect(x: side + list, y: 0, width: 1, height: height)
        brand.frame = NSRect(x: 22, y: 25, width: 122, height: 28); subtitle.frame = NSRect(x: 22, y: 55, width: 122, height: 18)
        for index in nav.indices { nav[index].frame = NSRect(x: 0, y: 91 + CGFloat(index) * 43, width: side, height: 43) }
        historyButton.frame = NSRect(x: 12, y: height - 119, width: side - 24, height: 30)
        localeButton.frame = NSRect(x: 12, y: height - 78, width: side - 24, height: 30)
        credit.frame = NSRect(x: 24, y: height - 26, width: 100, height: 14)
        middleTitle.frame = NSRect(x: 18, y: 29, width: list - 36, height: 25)
        let modeFirst = page == .games || page == .exams
        let oneFilter = page == .code || page == .library || page == .exams
        firstLabel.frame = NSRect(x: 18, y: 82, width: list - 36, height: 17)
        secondLabel.frame = NSRect(x: 18, y: 147, width: list - 36, height: 17)
        let first = modeFirst ? group : language, second = modeFirst ? language : group
        first.frame = NSRect(x: 16, y: 104, width: list - 32, height: 28); first.isHidden = false
        second.frame = NSRect(x: 16, y: 169, width: list - 32, height: 28); second.isHidden = oneFilter; secondLabel.isHidden = oneFilter
        let sy: CGFloat = oneFilter ? 148 : 213
        searchLabel.frame = NSRect(x: 18, y: sy, width: list - 36, height: 17)
        search.frame = NSRect(x: 18, y: sy + 25, width: list - 36, height: 27)
        lessonScroll.frame = NSRect(x: 8, y: sy + 68, width: list - 16, height: max(80, height - sy - 133))
        lessons.tableColumns.first?.width = list - 20
        libraryButton.isHidden = page != .library; libraryButton.frame = NSRect(x: 16, y: height - 48, width: list - 32, height: 30)
        textCount.isHidden = page == .library; textCount.frame = NSRect(x: 18, y: height - 43, width: list - 36, height: 21)
        titleLabel.frame = NSRect(x: x, y: 27, width: w, height: 30); descriptionLabel.frame = NSRect(x: x, y: 67, width: w, height: 38)
        let active = current != nil
        for view in [length, fontLabel, fontSize, modeLabel, paper, progress, nextButton, shuffleButton, restartButton] as [NSView] { view.isHidden = !active }
        for view in statLabels + statValues { view.isHidden = !active }
        length.frame = NSRect(x: x, y: 119, width: 250, height: 28)
        fontLabel.frame = NSRect(x: x + w - 128, y: 123, width: 65, height: 20); fontSize.frame = NSRect(x: x + w - 58, y: 118, width: 62, height: 28)
        for index in 0..<4 {
            statLabels[index].frame = NSRect(x: x + CGFloat(index) * w / 4, y: 167, width: w / 4 - 8, height: 17)
            statValues[index].frame = NSRect(x: x + CGFloat(index) * w / 4, y: 190, width: w / 4 - 8, height: 29)
        }
        modeLabel.frame = NSRect(x: x, y: 233, width: w - (page == .test ? 145 : 0), height: 20)
        duration.frame = NSRect(x: x + w - 135, y: 227, width: 138, height: 28); duration.isHidden = !active || page != .test
        pace.frame = NSRect(x: x + w - 142, y: 227, width: 145, height: 28); pace.isHidden = !active || current?.game != .race
        race.frame = NSRect(x: x, y: 229, width: w - 160, height: 34); race.isHidden = !active || current?.game != .race; modeLabel.isHidden = !active || current?.game == .race
        paper.frame = NSRect(x: x, y: 276, width: w, height: max(150, height - 391))
        practice.frame = NSRect(x: 14, y: 14, width: w - 28, height: paper.bounds.height - 28)
        progress.frame = NSRect(x: x, y: paper.frame.maxY - 2, width: w, height: 3)
        footer.frame = NSRect(x: x, y: paper.frame.maxY + 15, width: w, height: 23)
        let y = height - 52
        nextButton.frame = NSRect(x: x - 2, y: y, width: 104, height: 32); shuffleButton.frame = NSRect(x: x + 107, y: y, width: 89, height: 32)
        restartButton.frame = NSRect(x: x + w - 118, y: y, width: 122, height: 32)
        peekButton.frame = NSRect(x: x + w - 221, y: y, width: 98, height: 32); peekButton.isHidden = !active || current?.game != .memory
    }

    private func runAutomation() {
        do {
            guard catalogError.isEmpty, !catalog.isEmpty else { throw NSError(domain: "QuietType", code: 2, userInfo: [NSLocalizedDescriptionKey: catalogError]) }
            if CommandLine.arguments.contains("--smoke-test") {
                let input = PracticeView(frame: NSRect(x: 0, y: 0, width: 600, height: 240))
                let composingSession = try TypingSession(target: "a中文b")
                let absent = NSRange(location: NSNotFound, length: 0)
                input.load(composingSession, isCode: false)
                input.insertText("a", replacementRange: absent)
                input.setMarkedText("中文", selectedRange: NSRange(location: NSNotFound, length: Int.max), replacementRange: absent)
                let selection = input.selectedRange()
                guard composingSession.typed == "a", selection.location == 3, selection.length == 0 else { throw smokeError("IME pending text or absent selection changed the session") }
                input.unmarkText(); input.unmarkText()
                guard composingSession.typed == "a中文", composingSession.attempts == 3 else { throw smokeError("IME unmark did not commit exactly once") }
                input.setMarkedText("b", selectedRange: NSRange(location: Int.max - 1, length: Int.max), replacementRange: absent)
                guard input.selectedRange() == NSRange(location: 4, length: 0) else { throw smokeError("IME oversized selection was not clamped") }
                let nextSession = try TypingSession(target: "next")
                input.load(nextSession, isCode: false); input.unmarkText()
                guard nextSession.position == 0, composingSession.typed == "a中文" else { throw smokeError("IME composition leaked across rounds") }
                input.setMarkedText("n", selectedRange: NSRange(location: 1, length: 0), replacementRange: absent)
                input.doCommand(by: Selector(("cancelOperation:"))); input.unmarkText()
                guard nextSession.attempts == 0, nextSession.score == 0 else { throw smokeError("IME cancellation changed the score") }
                input.setMarkedText("n", selectedRange: NSRange(location: 0, length: 1), replacementRange: absent)
                input.clear(); input.unmarkText()
                guard nextSession.position == 0 else { throw smokeError("Clearing IME text committed a cancelled draft") }
                for value in pages { selectWorkspace(value) }
                selectWorkspace(.daily)
                guard let session = practice.session, let first = session.targetUnits.first else { throw smokeError("No daily session") }
                practice.insertText(first, replacementRange: NSRange(location: NSNotFound, length: 0))
                let typed = session.typed, elapsed = session.elapsedSeconds
                toggleLocale()
                guard practice.session === session, session.typed == typed, session.elapsedSeconds >= elapsed else { throw smokeError("Locale changed the session") }
                toggleLocale()
                for kind in [GameKind.combo, .suddenDeath, .memory, .race, .revenge] {
                    selectWorkspace(.games)
                    guard let lesson = catalog.first(where: { $0.game == kind }) else { throw smokeError("Missing challenge") }
                    loading = true; displayed = [lesson]; lessons.reloadData(); lessons.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false); loading = false
                    startCurrent(false)
                    if kind == .memory { beginMemory(); memoryStarted -= 9; tick(); peek(); memoryStarted -= 2; tick() }
                    guard practice.session != nil else { throw smokeError("Challenge could not start") }
                }
                let library = LibraryWindow(store: store, english: english); _ = library.window
                let history = HistoryWindow(store: store, english: english); _ = history.window
                selectWorkspace(.daily)
                print("Quiet Type AppKit smoke test passed")
            }
            if let index = CommandLine.arguments.firstIndex(of: "--screenshot"), CommandLine.arguments.indices.contains(index + 1) {
                root.layoutSubtreeIfNeeded(); root.displayIfNeeded()
                guard let bitmap = root.bitmapImageRepForCachingDisplay(in: root.bounds) else { throw smokeError("Cannot create window image") }
                root.cacheDisplay(in: root.bounds, to: bitmap)
                guard let png = bitmap.representation(using: .png, properties: [:]) else { throw smokeError("Cannot encode window image") }
                try png.write(to: URL(fileURLWithPath: CommandLine.arguments[index + 1]))
            }
            NSApp.terminate(nil)
        } catch { fputs("Quiet Type UI check failed: \(error.localizedDescription)\n", stderr); exit(1) }
    }
    private func smokeError(_ message: String) -> NSError { NSError(domain: "QuietType", code: 3, userInfo: [NSLocalizedDescriptionKey: message]) }
}

private final class RaceView: FlippedView {
    var english = false
    var actual = 0.0
    var ghost = 0.0
    var caption = ""
    override func draw(_ dirtyRect: NSRect) {
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: Ui.dim]
        (Ui.pick(english, "你", "You") as NSString).draw(at: NSPoint(x: 0, y: 0), withAttributes: attributes)
        (Ui.pick(english, "影子", "Ghost") as NSString).draw(at: NSPoint(x: 0, y: 17), withAttributes: attributes)
        let width = max(40, bounds.width - 120)
        for (index, amount) in [actual, ghost].enumerated() {
            let y = CGFloat(index * 17 + 8)
            NSColor(calibratedWhite: 0.91, alpha: 1).setFill(); NSRect(x: 40, y: y, width: width, height: 3).fill()
            (index == 0 ? Ui.ink : Ui.accent).setFill(); NSRect(x: 40, y: y, width: width * CGFloat(max(0, min(1, amount))), height: 3).fill()
        }
        (caption as NSString).draw(at: NSPoint(x: 48 + width, y: 8), withAttributes: attributes)
    }
}
