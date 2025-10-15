using Xunit;

// Testparallelisierung deaktiviert, weil mehrere Tests Prozess-weite Umgebungsvariablen
// (z.B. SPOCR_DISABLE_ENV_BOOTSTRAP, SPOCR_NAMESPACE, SPOCR_GENERATOR_MODE) setzen und
// zurücksetzen. Parallele Ausführung führte zu Race Conditions, bei denen erwartete
// Exceptions (fehlende .env bei deaktiviertem Bootstrap) ausblieben, weil ein anderer
// Test die Variable zwischenzeitlich entfernte.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
