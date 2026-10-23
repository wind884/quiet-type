import Foundation

final class UserStore {
    var data = UserData()
    private(set) var lastError = ""
    private let directory: URL
    private let file: URL
    private let maximumBytes = 4 * 1024 * 1024
    private var needsPreservedCopy = false
    private var loadAttempted = false
    private var acceptedData = UserData()

    init(directory: URL? = nil) {
        self.directory = directory ?? FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/QuietType", isDirectory: true)
        file = self.directory.appendingPathComponent("userdata.json")
    }

    @discardableResult
    func load() -> Bool {
        guard data == acceptedData else {
            lastError = "当前更改尚未保存，已保留在内存 / Unsaved changes remain in memory"
            return false
        }
        loadAttempted = true
        do {
            guard FileManager.default.fileExists(atPath: file.path) else { lastError = ""; return true }
            let attributes = try FileManager.default.attributesOfItem(atPath: file.path)
            if let size = attributes[.size] as? NSNumber, size.intValue > maximumBytes { throw StoreError.tooLarge }
            let bytes = try Data(contentsOf: file)
            guard bytes.count <= maximumBytes else { throw StoreError.tooLarge }
            let loaded = try JSONDecoder().decode(UserData.self, from: bytes)
            data = repaired(loaded)
            acceptedData = data
            needsPreservedCopy = !data.texts.allSatisfy { isValid($0) }
            lastError = needsPreservedCopy
                ? "有文本暂时无法练习，可在文本库中修改 / Some saved text needs editing in My texts" : ""
            return true
        } catch {
            needsPreservedCopy = true
            lastError = "无法读取本机记录，保存时会保留原文件副本 / Local records could not be read; an explicit save will preserve the original"
            return false
        }
    }

    @discardableResult
    func save() -> Bool {
        do {
            var candidate = repaired(data)
            for index in candidate.texts.indices {
                candidate.texts[index] = try validated(candidate.texts[index])
            }
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let bytes = try encoder.encode(candidate)
            guard bytes.count <= maximumBytes else { throw StoreError.tooLarge }
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            if FileManager.default.fileExists(atPath: file.path) && (needsPreservedCopy || !loadAttempted) {
                let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "")
                let backup = directory.appendingPathComponent("userdata.bad-\(stamp)-\(UUID().uuidString).json")
                try FileManager.default.copyItem(at: file, to: backup)
            }
            try bytes.write(to: file, options: .atomic)
            data = candidate; acceptedData = candidate
            needsPreservedCopy = false; loadAttempted = true; lastError = ""
            return true
        } catch {
            if case StoreError.tooLarge = error {
                lastError = "本机记录超过 4 MB，当前更改仍保留在内存 / Local data exceeds 4 MB; current changes remain in memory"
            } else if let textError = error as? PracticeTextError {
                lastError = (textError.errorDescription ?? "") + " / Current changes remain in memory"
            } else if case StoreError.incompleteText = error {
                lastError = "请填写标题、语言和正文，当前更改仍保留在内存 / Fill in the title, language and text; current changes remain in memory"
            } else {
                lastError = "无法写入本机记录，请检查目录权限，当前更改仍保留在内存 / Local records could not be saved; check folder access. Current changes remain in memory"
            }
            return false
        }
    }

    @discardableResult
    func saveText(_ text: SavedText) -> Bool {
        do {
            let text = try validated(text)
            if let index = data.texts.firstIndex(where: { $0.id == text.id }) { data.texts[index] = text }
            else { data.texts.insert(text, at: 0) }
            return save()
        } catch {
            lastError = (error as? LocalizedError)?.errorDescription
                ?? "请填写有效的标题、语言和正文 / Fill in a valid title, language and text"
            return false
        }
    }

    @discardableResult
    func deleteText(id: String) -> Bool {
        guard !id.isEmpty else { lastError = "请先选择一篇文本 / Select a saved text first"; return false }
        data.texts.removeAll { $0.id == id }
        return save()
    }

    @discardableResult
    func record(_ record: ScoreRecord, missedTokens: [String]) -> Bool {
        var record = record
        if record.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { record.id = UUID().uuidString }
        if record.whenIso.isEmpty { record.whenIso = ISO8601DateFormatter().string(from: Date()) }
        data.results.removeAll { $0.id == record.id }
        data.results.insert(record, at: 0)
        for token in missedTokens where !token.isEmpty {
            if let index = data.mistakes.firstIndex(where: { $0.text == token }) {
                data.mistakes[index].count = saturatingAdd(data.mistakes[index].count, 1)
            } else { data.mistakes.append(MistakeItem(text: token, count: 1)) }
        }
        return save()
    }

    @discardableResult
    func clearMistakes(_ tokens: [String]) -> Bool {
        let selected = Set(tokens)
        data.mistakes.removeAll { selected.contains($0.text) }
        return save()
    }

    private func isValid(_ text: SavedText) -> Bool { (try? validated(text)) != nil }

    private func validated(_ text: SavedText) throws -> SavedText {
        var text = text
        text.title = text.title.trimmingCharacters(in: .whitespacesAndNewlines)
        text.language = text.language.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.title.isEmpty && !text.language.isEmpty else { throw StoreError.incompleteText }
        text.text = try PracticeText.normalize(text.text, code: false)
        if text.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { text.id = UUID().uuidString }
        return text
    }

    private func repaired(_ source: UserData) -> UserData {
        var result = source
        result.fontSize = min(21, max(13, source.fontSize))
        result.uiLanguage = source.uiLanguage == "en" ? "en" : "zh"
        var textIDs: Set<String> = []
        for index in result.texts.indices {
            var id = result.texts[index].id
            if id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || textIDs.contains(id) { id = UUID().uuidString }
            result.texts[index].id = id
            textIDs.insert(id)
        }
        var recordIDs: Set<String> = []
        result.results = source.results.compactMap { source in
            var item = source
            if item.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { item.id = UUID().uuidString }
            guard recordIDs.insert(item.id).inserted else { return nil }
            if !item.cpm.isFinite || item.cpm < 0 { item.cpm = 0 }
            if !item.accuracy.isFinite || item.accuracy < 0 { item.accuracy = 0 }
            item.accuracy = min(100, item.accuracy)
            if !item.seconds.isFinite || item.seconds < 0 { item.seconds = 0 }
            item.score = max(0, item.score); item.bestCombo = max(0, item.bestCombo)
            item.typedCount = max(0, item.typedCount); item.correctCount = min(item.typedCount, max(0, item.correctCount))
            return item
        }
        result.results = Array(result.results.prefix(100))
        var counts: [String: Int] = [:]
        for item in source.mistakes where !item.text.isEmpty {
            counts[item.text] = saturatingAdd(counts[item.text] ?? 0, max(1, item.count))
        }
        result.mistakes = counts.map { MistakeItem(text: $0.key, count: $0.value) }
            .sorted { $0.count == $1.count ? $0.text < $1.text : $0.count > $1.count }
        result.mistakes = Array(result.mistakes.prefix(200))
        return result
    }

    private func saturatingAdd(_ value: Int, _ amount: Int) -> Int {
        if value >= Int.max - amount { return Int.max }
        return value + amount
    }

    private enum StoreError: LocalizedError {
        case tooLarge, incompleteText
        var errorDescription: String? {
            switch self {
            case .tooLarge: return "本机记录超过 4 MB / Local data exceeds 4 MB"
            case .incompleteText: return "请填写标题、语言和正文 / Fill in the title, language and text"
            }
        }
    }
}
