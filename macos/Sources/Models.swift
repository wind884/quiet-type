import Foundation

enum Section: Int, Codable, CaseIterable {
    case english = 0, chinese, custom, exams, test, code, games
}

enum GameKind: Int, Codable, CaseIterable {
    case none = 0, combo, suddenDeath, memory, race, revenge

    var catalogKey: String {
        switch self {
        case .none: return "None"
        case .combo: return "Combo"
        case .suddenDeath: return "SuddenDeath"
        case .memory: return "Memory"
        case .race: return "Race"
        case .revenge: return "Revenge"
        }
    }
}

enum Workspace: String, CaseIterable {
    case daily, exams, code, games, test, library
}

enum FinishReason {
    case none, completed, timeUp, lost
}

struct Choice: Equatable {
    var key: String
    var zh: String
    var en: String
}

struct Lesson: Codable, Equatable {
    var id = ""
    var title = ""
    var titleEn = ""
    var language = "英语"
    var shortText = ""
    var longText = ""
    var description = ""
    var descriptionEn = ""
    var sourceInfo = ""
    var sourceInfoEn = ""
    var category = ""
    var categoryEn = ""
    var section: Section = .english
    var isCode = false
    var game: GameKind = .none

    enum CodingKeys: String, CodingKey {
        case id = "Id", title = "Title", titleEn = "TitleEn", language = "Language"
        case shortText = "ShortText", longText = "LongText", description = "Description"
        case descriptionEn = "DescriptionEn", sourceInfo = "SourceInfo", sourceInfoEn = "SourceInfoEn"
        case category = "Category", categoryEn = "CategoryEn", section = "Section", isCode = "IsCode", game = "Game"
    }
}

struct SavedText: Codable, Equatable {
    var id = ""
    var title = ""
    var language = ""
    var text = ""

    enum CodingKeys: String, CodingKey {
        case id = "Id", title = "Title", language = "Language", text = "Text"
    }
}

struct ScoreRecord: Codable, Equatable {
    var id = ""
    var whenIso = ""
    var section = ""
    var title = ""
    var titleEn = ""
    var language = ""
    var lengthMode = ""
    var result = ""
    var cpm: Double = 0
    var accuracy: Double = 0
    var seconds: Double = 0
    var score = 0
    var bestCombo = 0
    var typedCount = 0
    var correctCount = 0

    enum CodingKeys: String, CodingKey {
        case id = "Id", whenIso = "WhenIso", section = "Section", title = "Title", titleEn = "TitleEn"
        case language = "Language", lengthMode = "LengthMode", result = "Result"
        case cpm = "Cpm", accuracy = "Accuracy", seconds = "Seconds", score = "Score"
        case bestCombo = "BestCombo", typedCount = "TypedCount", correctCount = "CorrectCount"
    }
}

struct MistakeItem: Codable, Equatable {
    var text = ""
    var count = 0

    enum CodingKeys: String, CodingKey { case text = "Text", count = "Count" }
}

struct UserData: Codable, Equatable {
    var texts: [SavedText] = []
    var results: [ScoreRecord] = []
    var mistakes: [MistakeItem] = []
    var fontSize = 15
    var uiLanguage = "zh"

    enum CodingKeys: String, CodingKey {
        case texts = "Texts", results = "Results", mistakes = "Mistakes", fontSize = "FontSize", uiLanguage = "UiLanguage"
    }
}

struct SessionOptions {
    var game: GameKind = .none
    var timeLimitSeconds = 0
    var mistakeLimit = 0
    var countWhileUnfocused = false
    var isCode = false
    var stageCount = 3
}

extension SavedText {
    init(from decoder: Decoder) throws {
        self.init()
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decodeIfPresent(String.self, forKey: .id) ?? id
        title = try values.decodeIfPresent(String.self, forKey: .title) ?? title
        language = try values.decodeIfPresent(String.self, forKey: .language) ?? language
        text = try values.decodeIfPresent(String.self, forKey: .text) ?? text
    }
}

extension ScoreRecord {
    init(from decoder: Decoder) throws {
        self.init()
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decodeIfPresent(String.self, forKey: .id) ?? id
        whenIso = try values.decodeIfPresent(String.self, forKey: .whenIso) ?? whenIso
        section = try values.decodeIfPresent(String.self, forKey: .section) ?? section
        title = try values.decodeIfPresent(String.self, forKey: .title) ?? title
        titleEn = try values.decodeIfPresent(String.self, forKey: .titleEn) ?? titleEn
        language = try values.decodeIfPresent(String.self, forKey: .language) ?? language
        lengthMode = try values.decodeIfPresent(String.self, forKey: .lengthMode) ?? lengthMode
        result = try values.decodeIfPresent(String.self, forKey: .result) ?? result
        cpm = try values.decodeIfPresent(Double.self, forKey: .cpm) ?? cpm
        accuracy = try values.decodeIfPresent(Double.self, forKey: .accuracy) ?? accuracy
        seconds = try values.decodeIfPresent(Double.self, forKey: .seconds) ?? seconds
        score = try values.decodeIfPresent(Int.self, forKey: .score) ?? score
        bestCombo = try values.decodeIfPresent(Int.self, forKey: .bestCombo) ?? bestCombo
        typedCount = try values.decodeIfPresent(Int.self, forKey: .typedCount) ?? typedCount
        correctCount = try values.decodeIfPresent(Int.self, forKey: .correctCount) ?? correctCount
    }
}

extension MistakeItem {
    init(from decoder: Decoder) throws {
        self.init()
        let values = try decoder.container(keyedBy: CodingKeys.self)
        text = try values.decodeIfPresent(String.self, forKey: .text) ?? text
        count = try values.decodeIfPresent(Int.self, forKey: .count) ?? count
    }
}

extension UserData {
    init(from decoder: Decoder) throws {
        self.init()
        let values = try decoder.container(keyedBy: CodingKeys.self)
        texts = try values.decodeIfPresent([SavedText].self, forKey: .texts) ?? texts
        results = try values.decodeIfPresent([ScoreRecord].self, forKey: .results) ?? results
        mistakes = try values.decodeIfPresent([MistakeItem].self, forKey: .mistakes) ?? mistakes
        fontSize = try values.decodeIfPresent(Int.self, forKey: .fontSize) ?? fontSize
        uiLanguage = try values.decodeIfPresent(String.self, forKey: .uiLanguage) ?? uiLanguage
    }
}
