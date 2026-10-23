import Foundation

#if MODEL_TESTS
private enum CheckError: Error { case failed(String) }

@main
struct ModelTests {
    private static var passed = 0
    private static var failed = 0

    private static func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        if !condition() { throw CheckError.failed(message) }
    }

    private static func rejects(_ action: () throws -> Void) throws {
        do { try action() } catch { return }
        throw CheckError.failed("Expected validation to reject text")
    }

    private static func run(_ name: String, _ body: () throws -> Void) {
        do { try body(); passed += 1; print("PASS \(name)") }
        catch { failed += 1; print("FAIL \(name): \(error)") }
    }

    static func main() throws {
        run("committed Chinese and Unicode graphemes") {
            let session = try TypingSession(target: "á👩‍💻你好，世界🇨🇳👍🏽")
            try check(session.commit("a\u{0301}👩‍💻你好"), "Committed batch rejected")
            try check(session.position == 4 && session.attempts == 4 && session.correctCount == 4, "A grapheme was split")
            _ = session.backspace()
            try check(session.typed == "á👩‍💻你", "Backspace removed part of a grapheme")
            _ = session.commit("好，世界🇨🇳👍🏽")
            try check(session.reason == .completed && session.errorCount == 0, "Unicode target not completed")
        }
        run("normalization preserves punctuation and code indentation") {
            let ordinary = try PracticeText.normalize(" \u{FEFF}Hello.\t 你好。\r\nNext\u{200B} ", code: false)
            try check(ordinary == "Hello. 你好。 Next", "Whitespace or punctuation changed")
            let code = try PracticeText.normalize("\nif (ok) {\r\n\trun();\r}\n", code: true)
            try check(code == "if (ok) {\n    run();\n}", "Code indentation or newline changed")
            let session = try TypingSession(target: code, options: SessionOptions(isCode: true))
            _ = session.commit("if (ok) {\n    run();\n}")
            try check(session.reason == .completed && session.errorCount == 0, "Code Enter was rejected")
        }
        run("validation rejects blank control RTL invalid and oversized text") {
            for value in [" \t\r\n", "مرحبا", "שלום", "a\u{0000}b", "bad\u{FFFD}", String(repeating: "中", count: 40_001)] {
                try rejects { _ = try PracticeText.normalize(value, code: false) }
            }
            let short = try PracticeText.shorten("你好世界👩‍💻", maximum: 3, code: false)
            try check(short == "你好世", "Shortening split a grapheme")
            let word = try PracticeText.shorten("one useful word and more", maximum: 12, code: false)
            try check(word == "one useful", "Shortening split a word unnecessarily")
            let code = try PracticeText.shorten("first\nsecond\nthird", maximum: 10, code: true)
            try check(code == "first", "Shortening did not prefer a code line")
        }
        run("word correction keeps cumulative accuracy and mistakes") {
            let session = try TypingSession(target: "hello world")
            _ = session.commit("hexx")
            try check(session.errorCount == 2 && session.totalMistakes == 2, "Incorrect attempts disappeared")
            _ = session.backspace(word: true)
            _ = session.commit("hello ")
            try check(session.typed == "hello " && session.errorCount == 0 && session.accuracy == 80, "Correction reset cumulative accuracy")
            try check(session.missedTokens == ["hello", "hello"], "Wrong word was not captured")
        }
        run("ordinary practice pauses and resumes") {
            var now: Double = 0
            let session = try TypingSession(target: "abcdef", now: { now })
            session.setActive(true); _ = session.commit("a")
            now = 5; session.setActive(false); now = 20
            try check(session.elapsedSeconds == 5 && !session.isRunning, "Background time was counted")
            session.setActive(true); now = 22
            try check(session.elapsedSeconds == 7 && session.isRunning, "Practice did not resume")
        }
        run("timed tests use wall time and freeze at deadline") {
            var now: Double = 10
            let session = try TypingSession(target: "abcdef", options: SessionOptions(timeLimitSeconds: 30, countWhileUnfocused: true), now: { now })
            session.setActive(true); _ = session.commit("a")
            now = 15; session.setActive(false); now = 42; session.tick()
            try check(session.reason == .timeUp && session.elapsedSeconds == 30, "Timed test paused or missed its deadline")
            try check(!session.commit("b") && !session.backspace(), "Timed result remained editable")
            try check(session.charactersPerMinute == 2, "CPM did not use elapsed time")
        }
        run("one life and revenge stop committed batches at the limit") {
            let one = try TypingSession(target: "中国文字", options: SessionOptions(game: .suddenDeath, mistakeLimit: 1))
            _ = one.commit("中错后续")
            try check(one.reason == .lost && one.position == 2 && !one.backspace(), "One life accepted input after failure")
            let revenge = try TypingSession(target: "abcdefghi", options: SessionOptions(game: .revenge, mistakeLimit: 3))
            _ = revenge.commit("xxxmore")
            try check(revenge.reason == .lost && revenge.totalMistakes == 3 && revenge.position == 3, "Revenge did not enforce its allowance")
        }
        run("combo rewards unique progress and requires ninety percent") {
            let session = try TypingSession(target: String(repeating: "a", count: 40), options: SessionOptions(game: .combo))
            _ = session.commit(String(repeating: "a", count: 20))
            let score = session.score
            try check(score > 20 && session.bestCombo == 20, "Combo multiplier missing")
            _ = session.backspace(); _ = session.commit("a")
            try check(session.score == score && session.bestCombo == 20, "Repeated positions farmed points")
            _ = session.commit("x")
            try check(session.combo == 0, "Wrong attempt did not reset combo")
            let pass = try TypingSession(target: "aaaaaaaaaa", options: SessionOptions(game: .combo))
            _ = pass.commit("aaaaaaaaax")
            try check(pass.reason == .completed && pass.accuracy == 90, "Ninety-percent boundary failed")
            let fail = try TypingSession(target: "aaaaaaaaaa", options: SessionOptions(game: .combo))
            _ = fail.commit("aaaaaaaaxx")
            try check(fail.reason == .lost, "Low-accuracy combo won")
        }
        run("memory race and revenge require a correct copy") {
            for game in [GameKind.memory, .race, .revenge] {
                let session = try TypingSession(target: "abcd", options: SessionOptions(game: game, timeLimitSeconds: game == .race ? 30 : 0))
                _ = session.commit("xxxx")
                try check(session.reason == .lost, "An incorrect copy won \(game)")
                let correct = try TypingSession(target: "abcd", options: SessionOptions(game: game))
                _ = correct.commit("abcd")
                try check(correct.reason == .completed, "Correct copy did not win \(game)")
            }
            var now: Double = 0
            let race = try TypingSession(target: "abcd", options: SessionOptions(game: .race, timeLimitSeconds: 5, countWhileUnfocused: true), now: { now })
            _ = race.commit("a"); now = 6; race.tick()
            try check(race.reason == .lost && race.elapsedSeconds == 5, "Race deadline did not lose")
        }
        run("only free practice completion is reversible") {
            let free = try TypingSession(target: "ab")
            _ = free.commit("ab")
            try check(free.reason == .completed && free.backspace() && !free.isFinished && free.typed == "a", "Free completion could not be corrected")
            let game = try TypingSession(target: "ab", options: SessionOptions(game: .combo))
            _ = game.commit("ab")
            try check(!game.backspace(), "Finished challenge remained editable")
            let test = try TypingSession(target: "ab", options: SessionOptions(timeLimitSeconds: 60))
            _ = test.commit("ab")
            try check(!test.backspace(), "Finished timed test remained editable")
        }
        run("catalogue schema and filters") {
            guard CommandLine.arguments.count > 1 else { throw CheckError.failed("Pass content/lessons.json") }
            let lessons = try Catalog.load(url: URL(fileURLWithPath: CommandLine.arguments[1]))
            try check(lessons.count == 201 && Set(lessons.map { $0.id }).count == lessons.count, "Catalogue count or IDs incorrect")
            let english = Catalog.query(lessons, page: .daily, language: "en", group: "", search: "", saved: [])
            try check(!english.isEmpty && english.allSatisfy { Catalog.languageKey($0) == "en" }, "Daily language filter failed")
            let games = Catalog.query(lessons, page: .games, language: "", group: "Combo", search: "", saved: [])
            try check(!games.isEmpty && games.allSatisfy { $0.game == .combo }, "Challenge group filter failed")
            let tests = Catalog.languages(lessons, page: .test, saved: [])
            try check(tests.map { $0.key } == ["en", "zh", "mixed"], "Timed languages incorrect")
            let saved = [SavedText(id: "valid", title: "My Note", language: "English", text: "Hello there."), SavedText(id: "invalid", title: "Bad", language: "en", text: "\u{0000}")]
            let library = Catalog.query(lessons, page: .library, language: "en", group: "", search: "note", saved: saved)
            try check(library.count == 1 && library[0].id == "user:valid", "Library failed validation or search")
            try check(PracticeText.tokenMatches("hello", language: "en") && !PracticeText.tokenMatches("你好hello", language: "en") && PracticeText.tokenMatches("你好", language: "zh"), "Mistake language filter failed")
        }
        run("store saves validated text and round trips PascalCase data") {
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
            defer { try? FileManager.default.removeItem(at: folder) }
            let store = UserStore(directory: folder)
            try check(store.load(), "Missing store failed to load")
            try check(store.saveText(SavedText(id: "mine", title: " Test ", language: " en ", text: "Hello\nworld")), "Text save failed")
            try check(!store.saveText(SavedText(id: "bad", title: "bad", language: "en", text: "שלום")), "Invalid text was saved")
            let loaded = UserStore(directory: folder)
            try check(loaded.load() && loaded.data.texts.count == 1 && loaded.data.texts[0].text == "Hello world", "Saved text did not round trip")
            let raw = try String(contentsOf: folder.appendingPathComponent("userdata.json"), encoding: .utf8)
            try check(raw.contains("\"Texts\"") && raw.contains("\"FontSize\""), "Store JSON keys changed")
            loaded.data.texts[0].text = "Unsaved edit"
            try check(!loaded.load() && loaded.data.texts[0].text == "Unsaved edit", "Load clobbered an unsaved edit")
            try check(loaded.deleteText(id: "mine") && loaded.data.texts.isEmpty, "Text deletion failed")
        }
        run("store caps results and merges bounded mistakes") {
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
            defer { try? FileManager.default.removeItem(at: folder) }
            let store = UserStore(directory: folder)
            store.data.results = (0..<110).map { ScoreRecord(id: "r\($0)", cpm: .infinity, accuracy: -2, seconds: .nan) }
            store.data.mistakes = (0..<205).map { MistakeItem(text: "word\($0)", count: $0 + 1) }
            store.data.mistakes.append(MistakeItem(text: "word204", count: Int.max))
            try check(store.save(), "Sanitized score save failed")
            try check(store.data.results.count == 100 && store.data.mistakes.count == 200 && store.data.mistakes[0].count == Int.max, "History or mistake cap failed")
            try check(store.data.results.allSatisfy { $0.cpm == 0 && $0.accuracy == 0 && $0.seconds == 0 }, "Nonfinite record values survived")
            try check(store.record(ScoreRecord(id: "result"), missedTokens: ["repeat", "repeat"]), "Result record failed")
            try check(store.data.results[0].id == "result", "Newest result was not first")
            try check(store.clearMistakes(store.data.mistakes.map { $0.text }) && store.data.mistakes.isEmpty, "Mistakes could not be cleared")
            try check(store.record(ScoreRecord(id: "again"), missedTokens: ["repeat", "repeat"]), "New mistakes could not be saved")
            try check(store.data.mistakes == [MistakeItem(text: "repeat", count: 2)], "Repeated mistakes did not merge")
        }
        run("incomplete files keep defaults and invalid texts remain editable") {
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
            defer { try? FileManager.default.removeItem(at: folder) }
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let raw = Data("{\"Texts\":[{\"Id\":\"bad\",\"Title\":\"Fix me\",\"Language\":\"en\",\"Text\":\"שלום\"}],\"FontSize\":100}".utf8)
            try raw.write(to: folder.appendingPathComponent("userdata.json"))
            let store = UserStore(directory: folder)
            try check(store.load() && store.data.fontSize == 21 && store.data.results.isEmpty, "Missing optional fields did not use defaults")
            try check(store.data.texts.count == 1 && !store.lastError.isEmpty, "Unusable saved text was silently discarded")
            try check(!store.save(), "Unusable saved text was accepted")
            try check(store.saveText(SavedText(id: "bad", title: "Fixed", language: "en", text: "Fixed text")), "Invalid loaded text could not be repaired")
            let backups = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil).filter { $0.lastPathComponent.hasPrefix("userdata.bad-") }
            try check(backups.count == 1, "Repair did not preserve the original")
            let backupBytes = try Data(contentsOf: backups[0])
            try check(backupBytes == raw, "Repair changed the preserved original")
        }
        run("a failed disk write retains the new text in memory") {
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
            defer { try? FileManager.default.removeItem(at: folder) }
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let blocker = folder.appendingPathComponent("blocked")
            try Data("file".utf8).write(to: blocker)
            let store = UserStore(directory: blocker)
            try check(!store.saveText(SavedText(id: "draft", title: "Draft", language: "en", text: "Keep this text")), "A file was used as a directory")
            try check(store.data.texts.first?.text == "Keep this text" && !store.lastError.isEmpty, "Failed save discarded the text")
            try check(!store.load() && store.data.texts.first?.text == "Keep this text", "Load discarded the failed save")
        }
        run("corrupt file is retained until explicit successful save") {
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
            defer { try? FileManager.default.removeItem(at: folder) }
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let file = folder.appendingPathComponent("userdata.json")
            let corrupt = Data("{broken".utf8)
            try corrupt.write(to: file)
            let store = UserStore(directory: folder)
            try check(!store.load() && !store.lastError.isEmpty, "Corrupt file loaded")
            let retainedBytes = try Data(contentsOf: file)
            let beforeSave = try FileManager.default.contentsOfDirectory(atPath: folder.path)
            try check(retainedBytes == corrupt, "Load modified corrupt data")
            try check(beforeSave.count == 1, "Load quarantined without a save")
            try check(store.saveText(SavedText(id: "new", title: "New", language: "en", text: "Safe text")), "Explicit recovery save failed")
            let preserved = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil).filter { $0.lastPathComponent.hasPrefix("userdata.bad-") }
            try check(preserved.count == 1, "Original corrupt file was not preserved")
            let preservedBytes = try Data(contentsOf: preserved[0])
            try check(preservedBytes == corrupt, "Preserved copy changed")
        }
        print("\(passed) passed, \(failed) failed")
        if failed > 0 { throw CheckError.failed("Model checks failed") }
    }
}
#endif
