using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Tests.Goals.Planning;

namespace RunnersDiary.Domain.Tests.Insights.Narrative;

/// <summary>
/// M10 C1 (ADR 0043 §3, plan §8): per-language <c>literalNumber</c> and <c>forbiddenTopic</c> rules. Adversarial Polish drafts per rule,
/// the everyday Polish words that must stay allowed, and English behaviour unchanged.
/// </summary>
public sealed class PolishNarrativeRulesTests
{
    private static readonly FactSheet Sheet = new(1, 1, "pl",
    [
        new Fact("totals.distance", FactValue.Of(42000), FactUnit.Meters, "42.0 km"),
        new Fact("totals.runCount", FactValue.Of(5), FactUnit.Count, "5"),
    ]);

    private static readonly SummaryValidationPolicy Pl = SummaryValidationPolicy.Default.ForLanguage("pl");
    private static readonly SummaryValidationPolicy En = SummaryValidationPolicy.Default;

    private static SummaryDraft Draft(string item) =>
        new("Dobry blok.", [new SummaryItem(item)], [], new SummaryItem("Trzymaj spokojne tempo."), []);

    private static IReadOnlyList<ValidationRuleCode> Codes(string item, SummaryValidationPolicy policy) =>
        SummaryValidator.Validate(Draft(item), Sheet, policy).Codes;

    // literalNumber, Polish

    [Theory]
    [InlineData("Masz za sobą {totals.distance} w dwa tygodnie.")]
    [InlineData("Łącznie: {totals.distance}, dwóch długich wybiegań.")]
    [InlineData("Łącznie: {totals.distance}, dwoch dlugich wybiegan.")] // without diacritics
    [InlineData("Łącznie: {totals.distance} przez trzy tygodnie.")]
    [InlineData("Łącznie: {totals.distance} w czterech tygodniach.")]
    [InlineData("Łącznie: {totals.distance}, pięć treningów.")]
    [InlineData("Łącznie: {totals.distance}, piec treningow.")]
    [InlineData("Łącznie: {totals.distance} z siedmioma treningami.")]
    [InlineData("Łącznie: {totals.distance} przez dziesięć dni.")]
    [InlineData("Łącznie: {totals.distance}, dwanaście sesji.")]
    [InlineData("Łącznie: {totals.distance} w ciągu piętnastu dni.")]
    [InlineData("Łącznie: {totals.distance}, dwadzieścia kilometrów.")]
    [InlineData("Łącznie: {totals.distance}, prawie pięćdziesiąt kilometrów.")]
    [InlineData("Łącznie: {totals.distance}, ponad pięćdziesięciu kilometrów.")]
    [InlineData("Łącznie: {totals.distance}, ponad sto kilometrów.")]
    [InlineData("Łącznie: {totals.distance}, dwieście minut.")]
    [InlineData("Łącznie: {totals.distance}, pięćset metrów przewyższeń.")]
    [InlineData("Łącznie: {totals.distance}, tysiąc metrów w górę.")]
    [InlineData("Łącznie: {totals.distance}, kilka tysięcy kroków.")]
    [InlineData("Łącznie: {totals.distance}, kilkanaście treningów.")]
    [InlineData("Łącznie: {totals.distance}, kilkadziesiąt kilometrów.")]
    [InlineData("Łącznie: {totals.distance}, półtora tygodnia.")]
    [InlineData("Łącznie: {totals.distance}, tuzin sesji.")]
    [InlineData("Łącznie: {totals.distance}, dwoje zawodów.")]
    [InlineData("Łącznie: {totals.distance}, w tym setki na stadionie.")]
    [InlineData("Łącznie: {totals.distance}, pobiegniesz dychę.")]
    [InlineData("Łącznie: {totals.distance}, cel to piątka.")]
    [InlineData("Pierwszy tydzień: {totals.distance}.")]
    [InlineData("Łącznie: {totals.distance} w drugim tygodniu.")]
    [InlineData("Łącznie: {totals.distance} w trzeciej części bloku.")]
    [InlineData("Łącznie: {totals.distance}, czwarta sesja była najmocniejsza.")]
    [InlineData("Łącznie: {totals.distance} w piątym tygodniu.")]
    [InlineData("Łącznie: {totals.distance} w ósmym tygodniu.")]
    [InlineData("Łącznie: {totals.distance} w dziesiątym tygodniu.")]
    [InlineData("Łącznie: {totals.distance} w dwudziestym tygodniu.")]
    [InlineData("Łącznie: {totals.distance}, objętość wzrosła dwukrotnie.")]
    [InlineData("Łącznie: {totals.distance}, objętość wzrosła podwójnie.")]
    [InlineData("Łącznie: {totals.distance} w trzytygodniowym bloku.")]
    [InlineData("Łącznie: {totals.distance}, dziesięciokilometrowy bieg.")]
    [InlineData("Łącznie: {totals.distance}, DWA długie biegi.")] // upper case
    [InlineData("Łącznie: {totals.distance}, two long runs.")] // English number word inside a Polish draft
    [InlineData("Łącznie: {totals.distance} w 3 tygodnie.")] // digits stay banned
    [InlineData("Łącznie: {totals.distance}, tydzień XII.")] // Roman numerals stay banned
    public void A_Polish_number_word_is_literal_number(string item)
    {
        Assert.Contains(ValidationRuleCode.LiteralNumber, Codes(item, Pl));
    }

    [Fact]
    public void A_number_word_in_decomposed_unicode_is_still_found()
    {
        var item = "Łącznie: {totals.distance}, pięć treningów.".Normalize(System.Text.NormalizationForm.FormD);
        Assert.Contains(ValidationRuleCode.LiteralNumber, Codes(item, Pl));
    }

    [Theory]
    [InlineData("Ten tydzień: {totals.distance}.")] // "ten" = "this"
    [InlineData("W piątek: {totals.distance}.")]
    [InlineData("W piątki i czwartki: {totals.distance}.")]
    [InlineData("Długi bieg w piątkach: {totals.distance}.")]
    [InlineData("Jeden długi bieg: {totals.distance}.")]
    [InlineData("Jedna mocna sesja: {totals.distance}.")]
    [InlineData("Pół godziny truchtu, łącznie: {totals.distance}.")]
    [InlineData("Połowa bloku za tobą: {totals.distance}.")]
    [InlineData("Przed półmaratonem: {totals.distance}.")]
    [InlineData("Trzymaj tempo, łącznie: {totals.distance}.")]
    [InlineData("Kilka spokojnych biegów: {totals.distance}.")]
    [InlineData("Oba długie biegi: {totals.distance}.")]
    [InlineData("Raz w tygodniu: {totals.distance}.")]
    [InlineData("Bieg wokół dworu: {totals.distance}.")]
    [InlineData("Stopa i pięta pracują: {totals.distance}.")]
    [InlineData("Stoper pokazał: {totals.distance}.")]
    [InlineData("Dwutlenek węgla w górach, łącznie: {totals.distance}.")]
    [InlineData("Długie wybieganie: {totals.distance}.")]
    public void Everyday_Polish_words_are_not_number_words(string item)
    {
        var codes = Codes(item, Pl);
        Assert.True(codes.Count == 0, item + ": " + string.Join(", ", codes));
    }

    [Fact]
    public void English_ten_stays_a_number_word_in_English_only()
    {
        Assert.Contains(ValidationRuleCode.LiteralNumber, Codes("You ran {totals.distance} in ten days.", En));
        Assert.DoesNotContain(ValidationRuleCode.LiteralNumber, Codes("Ten tydzień: {totals.distance}.", Pl));
    }

    [Fact]
    public void Polish_number_words_are_not_applied_to_English()
    {
        // "dwa", "sto", "trzy" are not English words; an English draft keeps exactly the English rules.
        Assert.Empty(Codes("You ran {totals.distance}, sto trzy dwa.", En));
    }

    [Fact]
    public void The_Polish_list_leaves_out_the_allowed_words()
    {
        string[] allowed =
        [
            "ten", "jeden", "jedna", "jedno", "jednego", "pol", "polowa", "polmaraton", "kilka", "oba", "obie", "raz", "piatek", "piatki",
            "piatkach", "piatkami", "piatkom", "piatku", "czwartek", "czwartki", "dwor", "trzymaj", "stopa", "stoper", "pieta", "set", "dysze",
            "zero", "tydzien", "trening", "tempo",
        ];
        Assert.All(allowed, w => Assert.False(PolishNumberWords.IsNumberWord(w), w));
        Assert.All(PolishNumberWords.Words, w => Assert.Equal(TextFolding.Fold(w), w));
    }

    // forbiddenTopic, Polish

    [Theory]
    [InlineData("Łącznie: {totals.distance}, uważaj na kontuzję.")]
    [InlineData("Łącznie: {totals.distance}, bez urazów.")]
    [InlineData("Łącznie: {totals.distance}, to ryzyko przetrenowania.")]
    [InlineData("Łącznie: {totals.distance}; jeśli czujesz ból, odpocznij.")]
    [InlineData("Łącznie: {totals.distance}; jesli czujesz bol, odpocznij.")] // without diacritics
    [InlineData("Łącznie: {totals.distance}; BÓL kolana to sygnał.")]
    [InlineData("Łącznie: {totals.distance}; gdy coś boli, zwolnij.")]
    [InlineData("Łącznie: {totals.distance}; bolesne łydki.")]
    [InlineData("Łącznie: {totals.distance}; skonsultuj się z lekarzem.")]
    [InlineData("Łącznie: {totals.distance}; odwiedź fizjoterapeutę.")]
    [InlineData("Łącznie: {totals.distance}; to nie diagnoza.")]
    [InlineData("Łącznie: {totals.distance}; ścięgno Achillesa.")]
    [InlineData("Łącznie: {totals.distance}; sciegno achillesa.")]
    [InlineData("Łącznie: {totals.distance}; zapalenie rozcięgna.")]
    [InlineData("Łącznie: {totals.distance}; możliwe złamanie zmęczeniowe.")]
    [InlineData("Łącznie: {totals.distance}; problem sercowy.")]
    [InlineData("Łącznie: {totals.distance}; ryzyko zawału.")]
    [InlineData("Łącznie: {totals.distance}; gdy jesteś chory, odpuść.")]
    [InlineData("Łącznie: {totals.distance}; nadwyrężenie łydki.")]
    [InlineData("Łącznie: {totals.distance}; naciągnięty mięsień.")]
    [InlineData("Łącznie: {totals.distance}; to sprawa medyczna.")]
    [InlineData("Łącznie: {totals.distance}; watch for pain.")] // English entries apply to Polish (code-switching)
    [InlineData("Łącznie: {totals.distance}; ask a doctor.")]
    [InlineData("Łącznie: {totals.distance}; the risk of injury.")]
    public void A_Polish_or_English_forbidden_topic_is_rejected_for_Polish(string item)
    {
        Assert.Contains(ValidationRuleCode.ForbiddenTopic, Codes(item, Pl));
    }

    [Fact]
    public void A_forbidden_topic_in_decomposed_unicode_is_still_found()
    {
        var item = "Łącznie: {totals.distance}; ból kolana.".Normalize(System.Text.NormalizationForm.FormD);
        Assert.Contains(ValidationRuleCode.ForbiddenTopic, Codes(item, Pl));
    }

    [Theory]
    [InlineData("Bolid na trasie, łącznie: {totals.distance}.")]
    [InlineData("Start w Bolesławcu, łącznie: {totals.distance}.")]
    [InlineData("Zapaleni biegacze lubią to: {totals.distance}.")]
    [InlineData("Zawalony tydzień, a jednak: {totals.distance}.")]
    [InlineData("Cel: złamanie {totals.distance}.")]
    [InlineData("Obciążenie i regeneracja: {totals.distance}.")]
    [InlineData("Zmęczenie po bloku, łącznie: {totals.distance}.")]
    [InlineData("Rozciąganie po biegu, łącznie: {totals.distance}.")]
    [InlineData("Biegniesz sercem, łącznie: {totals.distance}.")]
    [InlineData("Trening kardio, łącznie: {totals.distance}.")]
    [InlineData("Bez pośpiechu, łącznie: {totals.distance}.")]
    public void Ordinary_running_Polish_is_not_a_forbidden_topic(string item)
    {
        var codes = Codes(item, Pl);
        Assert.True(codes.Count == 0, item + ": " + string.Join(", ", codes));
    }

    [Fact]
    public void Polish_deny_entries_and_folding_are_not_applied_to_English()
    {
        // English stays byte-for-byte: "kontuzja" is not on the English list, and "páin" is not folded to "pain".
        Assert.Empty(Codes("You ran {totals.distance}, kontuzja aside.", En));
        Assert.Empty(Codes("You ran {totals.distance}, no páin.", En));
        Assert.Contains(ValidationRuleCode.ForbiddenTopic, Codes("You ran {totals.distance}, no pain.", En));
    }

    [Fact]
    public void A_manifest_Polish_list_replaces_the_default_one()
    {
        var custom = Pl with { LanguageDenyLists = new Dictionary<string, IReadOnlyList<string>> { ["pl"] = ["zmęczeni*"] } };
        Assert.Contains(ValidationRuleCode.ForbiddenTopic, Codes("Zmęczenie, łącznie: {totals.distance}.", custom));
        Assert.DoesNotContain(ValidationRuleCode.ForbiddenTopic, Codes("Łącznie: {totals.distance}, kontuzja.", custom));
        Assert.Contains(ValidationRuleCode.ForbiddenTopic, Codes("Łącznie: {totals.distance}, pain.", custom)); // en ∪ pl
    }

    [Fact]
    public void Every_supported_language_has_rules_and_others_are_rejected()
    {
        Assert.All(SupportedLanguages.All, l => Assert.True(NarrativeLanguages.IsSupported(l), l));
        Assert.Throws<ArgumentOutOfRangeException>(() => SummaryValidationPolicy.Default.ForLanguage("de"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanValidationPolicy.Default.ForLanguage("PL"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SummaryValidator.Validate(Draft("{totals.distance}"), Sheet, En with { Language = "de" }));
    }

    // Plan validator (forbiddenTopic only; digits are allowed in plan texts, ADR 0040)

    [Theory]
    [InlineData("Spokojnie, a przy bólu przerwij.")]
    [InlineData("Spokojnie, a przy bolu przerwij.")]
    [InlineData("Bez ryzyka kontuzji.")]
    [InlineData("Stop if you feel pain.")]
    public void A_Polish_plan_text_with_a_forbidden_topic_is_rejected(string text)
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        var sessions = TrainingPlanValidatorTests.Good(sheet);
        sessions[4] = sessions[4] with { Description = text };
        var policy = PlanValidationPolicy.Default.ForLanguage("pl");
        Assert.Contains(new PlanViolation(PlanRuleCode.ForbiddenTopic, "sessions[4].description"), TrainingPlanValidator.Validate(new TrainingPlanDraft("Plan.", sessions), sheet, policy).Violations);
        Assert.Contains(new PlanViolation(PlanRuleCode.ForbiddenTopic, "notes"), TrainingPlanValidator.Validate(new TrainingPlanDraft(text, TrainingPlanValidatorTests.Good(sheet)), sheet, policy).Violations);
    }

    [Theory]
    [InlineData("Spokojne wybieganie, 6 x 800 m w tempie na 5 km, trzymaj rytm.")]
    [InlineData("Zapaleni biegacze robią to w piątek: luźny bieg.")]
    [InlineData("Długie wybieganie w spokojnym tempie, cel: złamanie bariery.")]
    public void A_Polish_plan_text_without_a_forbidden_topic_is_valid(string text)
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        var sessions = TrainingPlanValidatorTests.Good(sheet);
        sessions[4] = sessions[4] with { Description = text };
        var result = TrainingPlanValidator.Validate(new TrainingPlanDraft(text, sessions), sheet, PlanValidationPolicy.Default.ForLanguage("pl"));
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
    }

    [Fact]
    public void A_Polish_plan_word_is_not_forbidden_in_English()
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        var result = TrainingPlanValidator.Validate(new TrainingPlanDraft("Bez kontuzji.", TrainingPlanValidatorTests.Good(sheet)), sheet, PlanValidationPolicy.Default);
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
    }
}
