import Foundation

enum CatalogError: LocalizedError {
    case invalidID
    var errorDescription: String? { "课程目录有重复或空白标识 / The lesson catalogue has a duplicate or missing ID" }
}

enum Catalog {
    static func load(url: URL) throws -> [Lesson] {
        let bytes = try Data(contentsOf: url)
        var lessons = try JSONDecoder().decode([Lesson].self, from: bytes)
        var ids: Set<String> = []
        for index in lessons.indices {
            guard !lessons[index].id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  ids.insert(lessons[index].id).inserted else { throw CatalogError.invalidID }
            lessons[index].shortText = try PracticeText.normalize(lessons[index].shortText, code: lessons[index].isCode)
            lessons[index].longText = try PracticeText.normalize(lessons[index].longText, code: lessons[index].isCode)
        }
        return lessons
    }

    static func languageKey(_ lesson: Lesson) -> String {
        if lesson.isCode { return lesson.language }
        let value = lesson.language.trimmingCharacters(in: .whitespacesAndNewlines)
        switch value.lowercased() {
        case "英语", "english", "en": return "en"
        case "中文", "chinese", "zh": return "zh"
        case "日本語", "日语", "japanese", "ja": return "ja"
        case "한국어", "韩语", "korean", "ko": return "ko"
        case "français", "法语", "french", "fr": return "fr"
        case "español", "西班牙语", "spanish", "es": return "es"
        case "deutsch", "德语", "german", "de": return "de"
        case "русский", "俄语", "russian", "ru": return "ru"
        case "混合", "混合文本", "混合语言", "mixed": return "mixed"
        default: return value
        }
    }

    static func query(_ lessons: [Lesson], page: Workspace, language: String, group: String,
                      search: String, saved: [SavedText]) -> [Lesson] {
        let candidates = page == .library ? saved.compactMap { savedLesson($0) } : lessons.filter { inPage($0, page: page) }
        let search = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return candidates.filter { lesson in
            if !language.isEmpty && languageKey(lesson) != language { return false }
            if !group.isEmpty {
                let value = page == .games ? lesson.game.catalogKey : page == .exams ? examGroup(lesson) : lesson.categoryEn
                if value != group { return false }
            }
            return search.isEmpty || lesson.title.range(of: search, options: .caseInsensitive) != nil
                || lesson.titleEn.range(of: search, options: .caseInsensitive) != nil
        }
    }

    static func languages(_ lessons: [Lesson], page: Workspace, saved: [SavedText]) -> [Choice] {
        let candidates = page == .library ? saved.compactMap { savedLesson($0) } : lessons.filter { inPage($0, page: page) }
        var keys: Set<String> = []
        var choices: [Choice] = []
        for lesson in candidates {
            let key = languageKey(lesson)
            if page == .test && !["en", "zh", "mixed"].contains(key) { continue }
            if keys.insert(key).inserted { choices.append(languageChoice(key)) }
        }
        return choices.sorted { languageOrder($0.key) < languageOrder($1.key) }
    }

    static func groups(page: Workspace) -> [Choice] {
        if page == .games {
            return [Choice(key: "Combo", zh: "连击闯关", en: "Combo run"),
                    Choice(key: "SuddenDeath", zh: "一命通关", en: "One life"),
                    Choice(key: "Memory", zh: "记忆闪打", en: "Memory flash"),
                    Choice(key: "Race", zh: "影子赛跑", en: "Ghost race"),
                    Choice(key: "Revenge", zh: "错字复仇", en: "Mistake rematch")]
        }
        if page == .exams {
            let kinds = ["IELTS Task 1", "IELTS Task 2", "TOEFL Email", "TOEFL Discussion", "CET-4", "CET-6"]
            return [Choice(key: "", zh: "全部题型", en: "All task types")] + kinds.map {
                Choice(key: $0, zh: $0 == "TOEFL Email" ? "托福邮件" : $0 == "TOEFL Discussion" ? "托福学术讨论" : $0, en: $0)
            }
        }
        let zh = ["生活", "旅行", "自然", "科学", "城市", "人物", "饮食", "微悬疑"]
        let en = ["Everyday", "Travel", "Nature", "Science", "Cities", "People", "Food", "Mystery"]
        return [Choice(key: "", zh: "全部分类", en: "All topics")] + en.indices.map { Choice(key: en[$0], zh: zh[$0], en: en[$0]) }
    }

    static func languageChoice(_ key: String) -> Choice {
        let values: [String: (String, String)] = [
            "en": ("英语", "English"), "zh": ("中文", "Chinese"), "mixed": ("混合文本", "Mixed text"),
            "ja": ("日语", "Japanese"), "ko": ("韩语", "Korean"), "fr": ("法语", "French"),
            "es": ("西班牙语", "Spanish"), "de": ("德语", "German"), "ru": ("俄语", "Russian")
        ]
        let text = values[key] ?? (key, key)
        return Choice(key: key, zh: text.0, en: text.1)
    }

    private static func savedLesson(_ saved: SavedText) -> Lesson? {
        guard !saved.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              !saved.language.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              let normalized = try? PracticeText.normalize(saved.text, code: false),
              let short = try? PracticeText.shorten(normalized, maximum: 160, code: false) else { return nil }
        var lesson = Lesson()
        lesson.id = "user:" + saved.id
        lesson.title = saved.title; lesson.titleEn = saved.title; lesson.language = saved.language
        lesson.shortText = short; lesson.longText = normalized; lesson.section = .custom
        lesson.category = "我的文本"; lesson.categoryEn = "My texts"
        lesson.description = "本机文本库"; lesson.descriptionEn = "Saved on this device"
        lesson.sourceInfo = lesson.description; lesson.sourceInfoEn = lesson.descriptionEn
        return lesson
    }

    private static func inPage(_ lesson: Lesson, page: Workspace) -> Bool {
        switch page {
        case .daily: return [Section.english, .chinese, .custom].contains(lesson.section)
        case .exams: return lesson.section == .exams
        case .code: return lesson.section == .code
        case .games: return lesson.section == .games
        case .test: return [Section.english, .chinese, .test].contains(lesson.section)
        case .library: return false
        }
    }

    private static func examGroup(_ lesson: Lesson) -> String {
        if lesson.id.contains("ielts-chart") { return "IELTS Task 1" }
        if lesson.id.contains("ielts-essay") { return "IELTS Task 2" }
        if lesson.id.contains("toefl-email") { return "TOEFL Email" }
        if lesson.id.contains("toefl-discussion") { return "TOEFL Discussion" }
        if lesson.id.contains("cet4") { return "CET-4" }
        if lesson.id.contains("cet6") { return "CET-6" }
        return lesson.categoryEn
    }

    private static func languageOrder(_ key: String) -> String {
        switch key { case "en": return "0"; case "zh": return "1"; case "mixed": return "2"; default: return "3" + key }
    }
}
