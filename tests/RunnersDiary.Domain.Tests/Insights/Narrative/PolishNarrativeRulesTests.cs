using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Tests.Goals.Planning;

namespace RunnersDiary.Domain.Tests.Insights.Narrative;

/// <summary>
/// M10 C1 (ADR 0043 §3, plan §8): per-language <c>literalNumber</c> and <c>forbiddenTopic</c> rules, and the Polish-only <c>genderedForm</c>
/// rule (G-L1 follow-up, plan §10). Adversarial Polish drafts per rule, the everyday Polish words that must stay allowed, and English behaviour unchanged.
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
    [InlineData("Łącznie: {totals.distance}, pięcio-kilometrowy bieg.")] // hyphenated compounds (review L1): folding splits them
    [InlineData("Łącznie: {totals.distance}, piecio-kilometrowy bieg.")]
    [InlineData("Łącznie: {totals.distance}, dziesięcio-kilometrowy bieg.")]
    [InlineData("Łącznie: {totals.distance}, cztero-tygodniowy blok.")]
    [InlineData("Łącznie: {totals.distance}, sześcio-, siedmio- lub ośmiodniowy cykl.")]
    [InlineData("Łącznie: {totals.distance}, dziewięcio-tygodniowy plan.")]
    [InlineData("Łącznie: {totals.distance}, jedenasto-kilometrowa pętla.")]
    [InlineData("Łącznie: {totals.distance}, dwunasto-tygodniowy plan.")]
    [InlineData("Łącznie: {totals.distance}, dwudziesto-minutowe tempo.")]
    [InlineData("Łącznie: {totals.distance}, trój-bój.")]
    [InlineData("Łącznie: {totals.distance}, kilkunasto-minutowe rozbieganie.")]
    [InlineData("Łącznie: {totals.distance}, kilkunastodniowy blok.")]
    [InlineData("Łącznie: {totals.distance}, kilkudziesięcio-minutowe wybieganie.")]
    [InlineData("Łącznie: {totals.distance}, kilkudziesieciominutowe wybieganie.")]
    [InlineData("Łącznie: {totals.distance}, sto-metrowe przebieżki.")]
    [InlineData("Łącznie: {totals.distance}, cel to jedenastka.")] // number nouns 11-19 and tens
    [InlineData("Łącznie: {totals.distance}, cel to dwunastka.")]
    [InlineData("Łącznie: {totals.distance}, po osiemnastce.")]
    [InlineData("Łącznie: {totals.distance}, po trzydziestce tempo spada.")]
    [InlineData("Łącznie: {totals.distance}, pięćdziesiątka w górach.")]
    [InlineData("Łącznie: {totals.distance}, piecdziesiatka w gorach.")]
    [InlineData("Łącznie: {totals.distance}, dwusetny trening.")] // hundreds, thousands, millions ordinals
    [InlineData("Łącznie: {totals.distance}, dwusetnego dnia.")]
    [InlineData("Łącznie: {totals.distance}, pięćsetny kilometr.")]
    [InlineData("Łącznie: {totals.distance}, piecsetny kilometr.")]
    [InlineData("Łącznie: {totals.distance}, trzechsetna sesja.")]
    [InlineData("Łącznie: {totals.distance}, dwutysięczny kilometr.")]
    [InlineData("Łącznie: {totals.distance}, milionowy krok.")]
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
    [InlineData("Jednodniowy obóz i kilkudniowy wyjazd: {totals.distance}.")]
    [InlineData("Z zapałem, łącznie: {totals.distance}.")]
    [InlineData("Na stadionie, łącznie: {totals.distance}.")]
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
            "zero", "tydzien", "trening", "tempo", "jednodniowy", "kilkudniowy", "kilku", "stopien", "osmoza", "czworaki", "trojkat",
            "stadion", "dwor", "milionerzy",
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
    [InlineData("Łącznie: {totals.distance}; możliwe złamanie przeciążeniowe.")] // review M1, with and without diacritics
    [InlineData("Łącznie: {totals.distance}; mozliwe zlamanie przeciazeniowe.")]
    [InlineData("Łącznie: {totals.distance}; po złamaniu nogi wracaj powoli.")]
    [InlineData("Łącznie: {totals.distance}; po zlamaniu nogi wracaj powoli.")]
    [InlineData("Łącznie: {totals.distance}; naderwanie łydki.")]
    [InlineData("Łącznie: {totals.distance}; naderwany mięsień.")]
    [InlineData("Łącznie: {totals.distance}; naderwanie lydki.")]
    [InlineData("Łącznie: {totals.distance}; stan zapalny kolana.")]
    [InlineData("Łącznie: {totals.distance}; proces zapalny stawu.")]
    [InlineData("Łącznie: {totals.distance}; zmiany zapalne.")]
    [InlineData("Łącznie: {totals.distance}; czas na rehabilitację.")]
    [InlineData("Łącznie: {totals.distance}; czas na rehabilitacje.")]
    [InlineData("Łącznie: {totals.distance}; wizyta u ortopedy.")]
    [InlineData("Łącznie: {totals.distance}; buty ortopedyczne.")]
    [InlineData("Łącznie: {totals.distance}; po pobycie w szpitalu.")]
    [InlineData("Łącznie: {totals.distance}; leczenie szpitalne.")]
    [InlineData("Łącznie: {totals.distance}; dolegliwości kolana.")]
    [InlineData("Łącznie: {totals.distance}; dolegliwosci kolana.")]
    [InlineData("Łącznie: {totals.distance}; DOLEGLIWOŚĆ stopy.")]
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
    [InlineData("Z zapałem, łącznie: {totals.distance}.")] // review M1: "zapał"/"zapalony" are not "zapaln*"
    [InlineData("Zapalony biegacz, łącznie: {totals.distance}.")]
    [InlineData("Zapalona drużyna, łącznie: {totals.distance}.")]
    [InlineData("Mocne nogi, łącznie: {totals.distance}.")]
    [InlineData("Cel: złamanie godziny, łącznie: {totals.distance}.")]
    [InlineData("Przeciążenie tygodnia widać w danych: {totals.distance}.")]
    [InlineData("Szpic formy przed startem, łącznie: {totals.distance}.")]
    [InlineData("Ortodroma trasy, łącznie: {totals.distance}.")]
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
    [InlineData("Po rehabilitacji wracaj spokojnie.")]
    [InlineData("Przy dolegliwosciach przerwij.")]
    [InlineData("Przy stanie zapalnym odpuść.")]
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
    [InlineData("Z zapałem, ale spokojnie: mocne nogi na podbiegach.")]
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

    // genderedForm, Polish only (G-L1 follow-up)

    [Theory]
    [InlineData("Ukończyłeś start z wynikiem {totals.distance}.")] // the G-L1 forms
    [InlineData("Na koniec utrzymałeś równe tempo: {totals.distance}.")]
    [InlineData("W tym tygodniu zrobiłeś tapering, łącznie: {totals.distance}.")]
    [InlineData("Potem zakończyłeś blok, łącznie: {totals.distance}.")]
    [InlineData("Przebiegłeś łącznie {totals.distance}.")]
    [InlineData("Zrobiłaś łącznie {totals.distance}.")] // feminine past
    [InlineData("Byłeś regularny: {totals.distance}.")]
    [InlineData("Byłaś regularna: {totals.distance}.")]
    [InlineData("Wziąłeś się do pracy: {totals.distance}.")]
    [InlineData("Mogłeś odpocząć, łącznie: {totals.distance}.")]
    [InlineData("Mógłbyś dodać spokojny bieg, łącznie: {totals.distance}.")] // conditional
    [InlineData("Mogłabyś dodać spokojny bieg, łącznie: {totals.distance}.")]
    [InlineData("Zrobiłbyś więcej, łącznie: {totals.distance}.")]
    [InlineData("Moglbys dodac spokojny bieg, lacznie: {totals.distance}.")] // conditional without diacritics
    [InlineData("Moglabys dodac spokojny bieg, lacznie: {totals.distance}.")]
    [InlineData("Powinieneś odpocząć, łącznie: {totals.distance}.")] // modal
    [InlineData("Powinnaś odpocząć, łącznie: {totals.distance}.")]
    [InlineData("Powinienes odpoczac, lacznie: {totals.distance}.")]
    [InlineData("UKOŃCZYŁEŚ START: {totals.distance}.")] // upper case
    public void A_gendered_second_person_form_is_rejected_for_Polish(string item)
    {
        Assert.Contains(ValidationRuleCode.GenderedForm, Codes(item, Pl));
    }

    [Fact]
    public void A_gendered_form_in_decomposed_unicode_is_still_found()
    {
        var item = "Ukończyłeś start, łącznie: {totals.distance}.".Normalize(System.Text.NormalizationForm.FormD);
        Assert.Contains(ValidationRuleCode.GenderedForm, Codes(item, Pl));
    }

    [Theory]
    [InlineData("Jesteś w formie, łącznie: {totals.distance}.")] // neutral second person
    [InlineData("Cześć! Łącznie: {totals.distance}.")]
    [InlineData("Możesz dodać spokojny bieg, łącznie: {totals.distance}.")]
    [InlineData("Masz za sobą {totals.distance}.")]
    [InlineData("Zapiszesz kolejny trening, łącznie: {totals.distance}.")] // future
    [InlineData("Zrobisz to spokojnie, łącznie: {totals.distance}.")]
    [InlineData("Zapisano treningi, łącznie: {totals.distance}.")] // impersonal
    [InlineData("Tydzień był lżejszy, obciążenie wzrosło: {totals.distance}.")] // a thing as the subject
    [InlineData("Bieg przez wieś i las, łącznie: {totals.distance}.")]
    [InlineData("Atlas tras, łącznie: {totals.distance}.")]
    [InlineData("Zrobiłem, zrobiłam, zrobił, zrobiła: {totals.distance}.")] // first and third person
    [InlineData("Żebyś biegał spokojnie, gdybyś chciał: {totals.distance}.")] // known limit: a past form after a separate "byś"
    [InlineData("Jeśli chcesz, dodaj przebieżki: {totals.distance}.")]
    [InlineData("Byłoby dobrze dodać spokojny bieg: {totals.distance}.")]
    [InlineData("Zrobiles łącznie {totals.distance}.")] // known limit: the past tense without diacritics (see PolishGenderedForms)
    public void Neutral_Polish_and_known_limits_are_not_gendered_forms(string item)
    {
        Assert.DoesNotContain(ValidationRuleCode.GenderedForm, Codes(item, Pl));
    }

    [Theory]
    [InlineData("jesteś")]
    [InlineData("cześć")]
    [InlineData("wieś")]
    [InlineData("możesz")]
    [InlineData("masz")]
    [InlineData("las")]
    [InlineData("miles")]
    [InlineData("żebyś")]
    [InlineData("gdybyś")]
    [InlineData("abyś")]
    [InlineData("jakbyś")]
    [InlineData("łeś")] // a bare suffix is not a word form
    public void Common_words_are_not_gendered(string word) => Assert.False(PolishGenderedForms.IsGendered(word), word);

    [Fact]
    public void The_gendered_form_rule_is_Polish_only()
    {
        Assert.DoesNotContain(ValidationRuleCode.GenderedForm, Codes("You ran {totals.distance}, ukończyłeś.", En));
    }

    [Theory]
    [InlineData("W tym tygodniu zrobiłeś dużo, teraz spokojnie.")]
    [InlineData("Gdy poczujesz zmęczenie, mogłabyś skrócić bieg.")]
    [InlineData("Powinienes zaczac spokojnie.")]
    public void A_Polish_plan_text_with_a_gendered_form_is_rejected(string text)
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        var sessions = TrainingPlanValidatorTests.Good(sheet);
        sessions[4] = sessions[4] with { Description = text };
        var policy = PlanValidationPolicy.Default.ForLanguage("pl");
        Assert.Contains(new PlanViolation(PlanRuleCode.GenderedForm, "sessions[4].description"), TrainingPlanValidator.Validate(new TrainingPlanDraft("Plan.", sessions), sheet, policy).Violations);
        Assert.Contains(new PlanViolation(PlanRuleCode.GenderedForm, "notes"), TrainingPlanValidator.Validate(new TrainingPlanDraft(text, TrainingPlanValidatorTests.Good(sheet)), sheet, policy).Violations);

        // English plans never get the rule.
        Assert.True(TrainingPlanValidator.Validate(new TrainingPlanDraft(text, TrainingPlanValidatorTests.Good(sheet)), sheet, PlanValidationPolicy.Default).IsValid);
    }

    [Fact]
    public void A_neutral_Polish_plan_text_is_valid()
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        const string text = "Jesteś w dobrym miejscu: zacznij spokojnie, przyspiesz w końcówce, a potem masz lżejszy dzień.";
        var result = TrainingPlanValidator.Validate(new TrainingPlanDraft(text, TrainingPlanValidatorTests.Good(sheet)), sheet, PlanValidationPolicy.Default.ForLanguage("pl"));
        Assert.True(result.IsValid, string.Join(", ", result.Violations));
    }
}
