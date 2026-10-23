import AppKit

final class LibraryWindow: NSWindowController, NSWindowDelegate, NSTableViewDataSource, NSTableViewDelegate, NSTextFieldDelegate, NSTextViewDelegate, NSComboBoxDelegate {
    private let store: UserStore
    private let english: Bool
    private let canvas = CanvasView()
    private let table = NSTableView()
    private var listScroll: NSScrollView!
    private let titleField = NSTextField()
    private let languageField = NSComboBox()
    private let editor = NSTextView()
    private var editorScroll: NSScrollView!
    private let libraryLabel: NSTextField
    private let titleLabel: NSTextField
    private let languageLabel: NSTextField
    private let contentLabel: NSTextField
    private let count = Ui.label(size: 10, color: Ui.dim)
    private let status = Ui.label(size: 11, color: Ui.dim)
    private var newButton: NSButton!
    private var importButton: NSButton!
    private var deleteButton: NSButton!
    private var saveButton: NSButton!
    private var useButton: NSButton!
    private var cancelButton: NSButton!
    private var loading = false
    private var dirty = false
    private var editingID = ""
    private var originalLanguage = "中文"
    private var originalLanguageLabel = ""
    private var selectedID: String?
    private let languages = ["中文", "英语", "日本語", "한국어", "Español", "Français", "Deutsch", "Русский"]
    private func t(_ zh: String, _ en: String) -> String { Ui.pick(english, zh, en) }

    init(store: UserStore, english: Bool) {
        self.store = store; self.english = english
        libraryLabel = Ui.label(Ui.pick(english, "文本库", "Text library"), bold: true)
        titleLabel = Ui.label(Ui.pick(english, "标题", "Title"), color: Ui.dim)
        languageLabel = Ui.label(Ui.pick(english, "语言，可直接输入自定义语言", "Language (or type your own)"), color: Ui.dim)
        contentLabel = Ui.label(Ui.pick(english, "正文", "Text"), color: Ui.dim)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 920, height: 600), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = Ui.pick(english, "自定义文本", "My texts")
        window.minSize = NSSize(width: 790, height: 510)
        window.isReleasedWhenClosed = false; window.delegate = self
        window.appearance = NSAppearance(named: .aqua)
        window.contentView = canvas
        canvas.onLayout = { [weak self] in self?.layout() }
        build()
        setEditor(store.data.texts.first)
    }
    required init?(coder: NSCoder) { return nil }

    private func build() {
        newButton = Ui.button(t("新建", "New"), target: self, action: #selector(newText))
        importButton = Ui.button(t("导入 TXT", "Import TXT"), target: self, action: #selector(importText))
        deleteButton = Ui.button(t("删除", "Delete"), target: self, action: #selector(deleteText))
        saveButton = Ui.button(t("保存", "Save"), target: self, action: #selector(save))
        useButton = Ui.button(t("保存并使用", "Save and use"), target: self, action: #selector(saveAndUse))
        cancelButton = Ui.button(t("关闭", "Close"), target: self, action: #selector(closeDialog))
        cancelButton.keyEquivalent = "\u{1b}"; useButton.keyEquivalent = "\r"
        for view in [libraryLabel, titleLabel, languageLabel, contentLabel, count, status, titleField, languageField, newButton, importButton, deleteButton, saveButton, useButton, cancelButton] as [NSView] { canvas.addSubview(view) }
        titleField.font = .systemFont(ofSize: 13); titleField.delegate = self
        titleField.setAccessibilityLabel(t("标题", "Title"))
        languageField.font = .systemFont(ofSize: 12); languageField.isEditable = true; languageField.usesDataSource = false
        languageField.addItems(withObjectValues: languages.map { Ui.language($0, english) })
        languageField.delegate = self; languageField.setAccessibilityLabel(t("语言", "Language"))
        table.addTableColumn(NSTableColumn(identifier: NSUserInterfaceItemIdentifier("text")))
        table.headerView = nil; table.rowHeight = 34; table.intercellSpacing = .zero
        table.dataSource = self; table.delegate = self
        table.setAccessibilityLabel(t("文本库", "Text library"))
        listScroll = Ui.scroll(table); listScroll.borderType = .lineBorder; canvas.addSubview(listScroll)
        editor.isRichText = false; editor.font = .systemFont(ofSize: 14); editor.textColor = Ui.ink
        editor.isEditable = true; editor.isSelectable = true; editor.allowsUndo = true
        editor.isAutomaticQuoteSubstitutionEnabled = false; editor.isAutomaticDashSubstitutionEnabled = false
        editor.isAutomaticSpellingCorrectionEnabled = false; editor.isAutomaticTextReplacementEnabled = false
        editor.textContainerInset = NSSize(width: 8, height: 8)
        editor.isVerticallyResizable = true; editor.isHorizontallyResizable = false
        editor.autoresizingMask = [.width]
        editor.textContainer?.widthTracksTextView = true
        editor.delegate = self; editor.setAccessibilityLabel(t("正文", "Text"))
        editorScroll = Ui.scroll(editor); editorScroll.borderType = .lineBorder; editorScroll.drawsBackground = true; editorScroll.backgroundColor = .white
        canvas.addSubview(editorScroll)
    }
    func run(parent: NSWindow) -> String? {
        guard let window = window else { return nil }
        window.setFrameOrigin(NSPoint(x: parent.frame.midX - window.frame.width / 2, y: parent.frame.midY - window.frame.height / 2))
        window.makeKeyAndOrderFront(nil)
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return selectedID
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard resolveEdits() else { return false }
        if NSApp.modalWindow === sender { NSApp.stopModal() }
        return true
    }
    func windowWillClose(_ notification: Notification) { if NSApp.modalWindow === window { NSApp.stopModal() } }
    @objc private func closeDialog() { window?.performClose(nil) }
    private func layout() {
        let width = canvas.bounds.width, height = canvas.bounds.height
        let left: CGFloat = english ? 278 : 250, right = left + 35, editorWidth = width - right - 20
        libraryLabel.frame = NSRect(x: 20, y: 20, width: left, height: 22)
        newButton.frame = NSRect(x: 17, y: 48, width: 68, height: 29)
        importButton.frame = NSRect(x: 86, y: 48, width: 104, height: 29)
        deleteButton.frame = NSRect(x: 191, y: 48, width: left - 171, height: 29)
        listScroll.frame = NSRect(x: 20, y: 88, width: left, height: height - 181)
        table.tableColumns.first?.width = left - 5
        titleLabel.frame = NSRect(x: right, y: 20, width: editorWidth, height: 20)
        titleField.frame = NSRect(x: right, y: 47, width: editorWidth, height: 26)
        languageLabel.frame = NSRect(x: right, y: 85, width: editorWidth, height: 20)
        languageField.frame = NSRect(x: right, y: 112, width: editorWidth, height: 26)
        contentLabel.frame = NSRect(x: right, y: 150, width: editorWidth, height: 20)
        editorScroll.frame = NSRect(x: right, y: 177, width: editorWidth, height: height - 296)
        editor.minSize = NSSize(width: editorWidth - 18, height: editorScroll.contentSize.height)
        editor.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        editor.setFrameSize(NSSize(width: editorScroll.contentSize.width, height: max(editor.frame.height, editorScroll.contentSize.height)))
        count.frame = NSRect(x: right, y: height - 112, width: editorWidth, height: 18); count.alignment = .right
        status.frame = NSRect(x: 20, y: height - 79, width: width - 40, height: 20)
        cancelButton.frame = NSRect(x: width - 106, y: height - 46, width: 89, height: 30)
        useButton.frame = NSRect(x: width - 250, y: height - 46, width: 139, height: 30)
        saveButton.frame = NSRect(x: width - 343, y: height - 46, width: 88, height: 30)
    }
    private func setEditor(_ text: SavedText?) {
        loading = true
        editingID = text?.id ?? ""; titleField.stringValue = text?.title ?? ""
        originalLanguage = text?.language ?? "中文"; originalLanguageLabel = Ui.language(originalLanguage, english)
        languageField.stringValue = originalLanguageLabel; editor.string = text?.text ?? ""
        editor.undoManager?.removeAllActions()
        table.reloadData()
        if let index = store.data.texts.firstIndex(where: { $0.id == editingID }) { table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false) } else { table.deselectAll(nil) }
        loading = false; dirty = false; updateCount()
        status.stringValue = text == nil ? t("新建文本后保存即可用于练习", "Save a new text to use it for practice") : t("编辑后保存，或直接保存并使用", "Edit and save, or choose Save and use")
    }
    private func edited() { guard !loading else { return }; dirty = true; status.stringValue = t("有未保存的修改", "Unsaved changes"); updateCount() }
    func controlTextDidChange(_ obj: Notification) { edited() }
    func textDidChange(_ notification: Notification) { edited() }
    func comboBoxSelectionDidChange(_ notification: Notification) { edited() }
    private func updateCount() {
        let n = PracticeText.elements(editor.string).count
        count.stringValue = "\(n.formatted())" + t(" / 40,000 字符", " / 40,000 characters")
        count.textColor = n > 40000 ? .systemRed : Ui.dim
    }
    private func resolveEdits() -> Bool {
        guard dirty else { return true }
        let alert = NSAlert(); alert.messageText = t("保存对当前文本的修改？", "Save changes to this text?")
        alert.addButton(withTitle: t("保存", "Save")); alert.addButton(withTitle: t("不保存", "Discard")); alert.addButton(withTitle: t("取消", "Cancel"))
        let result = alert.runModal()
        if result == .alertFirstButtonReturn { return saveEditor() }
        return result == .alertSecondButtonReturn
    }
    private func showError(_ message: String) {
        status.stringValue = message
        let alert = NSAlert(); alert.messageText = t("自定义文本", "My texts"); alert.informativeText = message
        alert.addButton(withTitle: t("确定", "OK")); alert.runModal()
    }
    private func canonicalLanguage() -> String {
        let value = languageField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        if value == originalLanguageLabel { return originalLanguage }
        return languages.first(where: { $0 == value || Ui.language($0, english).caseInsensitiveCompare(value) == .orderedSame }) ?? value
    }
    @discardableResult private func saveEditor() -> Bool {
        do {
            let title = titleField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            let language = canonicalLanguage()
            guard !title.isEmpty, !language.isEmpty else { showError(t("请填写标题、语言和正文", "Enter a title, language and text")); return false }
            guard title.count <= 120, language.count <= 60 else { showError(t("标题最多 120 字符，语言最多 60 字符", "Use up to 120 characters for the title and 60 for the language")); return false }
            let normalized = try PracticeText.normalize(editor.string, code: false)
            let id = editingID.isEmpty ? UUID().uuidString : editingID
            let text = SavedText(id: id, title: title, language: language, text: normalized)
            let success = store.saveText(text)
            editingID = id
            table.reloadData()
            guard success else { dirty = true; showError(Ui.error(store.lastError, english)); return false }
            setEditor(text); status.stringValue = t("已保存到本机", "Saved on this computer")
            return true
        } catch { showError(Ui.error(error.localizedDescription, english)); return false }
    }
    @objc private func save() { saveEditor() }
    @objc private func saveAndUse() {
        if saveEditor() { selectedID = editingID; NSApp.stopModal(); window?.close() }
    }
    @objc private func newText() { if resolveEdits() { setEditor(nil); window?.makeFirstResponder(titleField) } }
    @objc private func deleteText() {
        guard store.data.texts.indices.contains(table.selectedRow) else { showError(t("请先选择一篇已保存的文本", "Select a saved text first")); return }
        let selected = store.data.texts[table.selectedRow]
        let alert = NSAlert(); alert.messageText = t("删除「\(selected.title)」？", "Delete “\(selected.title)”?")
        alert.addButton(withTitle: t("删除", "Delete")); alert.addButton(withTitle: t("取消", "Cancel"))
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let success = store.deleteText(id: selected.id)
        setEditor(store.data.texts.first)
        if success { status.stringValue = t("已删除文本", "Text deleted") } else { showError(Ui.error(store.lastError, english)) }
    }
    @objc private func importText() {
        let picker = NSOpenPanel(); picker.title = t("导入文本", "Import text")
        picker.allowedFileTypes = ["txt"]; picker.allowsMultipleSelection = false; picker.canChooseDirectories = false
        guard picker.runModal() == .OK, let url = picker.url else { return }
        do {
            let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
            guard ((attributes[.size] as? NSNumber)?.intValue ?? 0) <= 128 * 1024 else { showError(t("文本文件最多 128 KB", "Text files can be up to 128 KB")); return }
            let data = try Data(contentsOf: url)
            guard data.count <= 128 * 1024 else { showError(t("文本文件最多 128 KB", "Text files can be up to 128 KB")); return }
            var imported: String?
            if data.starts(with: [0xff, 0xfe, 0, 0]) || data.starts(with: [0, 0, 0xfe, 0xff]) { imported = String(data: data, encoding: .utf32) }
            else if data.starts(with: [0xff, 0xfe]) || data.starts(with: [0xfe, 0xff]) { imported = String(data: data, encoding: .utf16) }
            else { imported = String(data: data, encoding: .utf8) }
            guard var text = imported else { showError(t("请使用 UTF-8 或带编码标记的 Unicode 文本文件", "Use UTF-8 or a Unicode text file with an encoding marker")); return }
            if text.first == "\u{feff}" { text.removeFirst() }
            _ = try PracticeText.normalize(text, code: false)
            guard resolveEdits() else { return }
            setEditor(nil); loading = true
            titleField.stringValue = url.deletingPathExtension().lastPathComponent; editor.string = text
            loading = false; dirty = true; updateCount(); status.stringValue = t("文本已导入，保存后可用于练习", "Text imported; save it to use it for practice")
        } catch { showError(Ui.error(error.localizedDescription, english)) }
    }
    func numberOfRows(in tableView: NSTableView) -> Int { store.data.texts.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard store.data.texts.indices.contains(row) else { return nil }
        let value = store.data.texts[row], label = Ui.label(size: 12)
        label.stringValue = Ui.language(value.language, english) + " / " + value.title
        return label
    }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !loading, store.data.texts.indices.contains(table.selectedRow) else { return }
        let target = store.data.texts[table.selectedRow]
        guard target.id != editingID else { return }
        if resolveEdits() { setEditor(target) }
        else {
            loading = true
            if let index = store.data.texts.firstIndex(where: { $0.id == editingID }) { table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false) } else { table.deselectAll(nil) }
            loading = false
        }
    }
}
