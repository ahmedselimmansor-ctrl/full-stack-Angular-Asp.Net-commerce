package com.sama.shop

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

/**
 * Keeps the Arabic string set aligned with the English one.
 *
 * Android silently falls back to the default resource when a translation is
 * missing, so an untranslated string is invisible until an Arabic-speaking
 * customer meets an English sentence in the middle of an Arabic screen. Nothing
 * fails, nothing logs.
 *
 * The files are read from the source tree rather than through the resource
 * system, because a JVM unit test has no resource table and this needs to
 * compare the two sets rather than resolve one of them.
 */
class TranslationsTest {

    private fun names(path: String): Map<String, String> {
        val file = File(path)
        assertTrue("expected to find $path", file.exists())

        val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val strings = document.getElementsByTagName("string")

        return (0 until strings.length).associate { index ->
            val node = strings.item(index)
            node.attributes.getNamedItem("name").nodeValue to node.textContent
        }
    }

    private val english = names("src/main/res/values/strings.xml")
    private val arabic = names("src/main/res/values-ar/strings.xml")

    /** The app name is a proper noun and stays as it is in both. */
    private val untranslated = setOf("app_name")

    @Test
    fun `every English string has an Arabic translation`() {
        val missing = (english.keys - untranslated) - arabic.keys
        assertEquals("missing Arabic translations", emptySet<String>(), missing)
    }

    @Test
    fun `no Arabic string names a key the English set does not have`() {
        // A stale key is dead weight that reads as coverage.
        val orphaned = arabic.keys - english.keys
        assertEquals("Arabic keys with no English source", emptySet<String>(), orphaned)
    }

    /**
     * A string carrying no letters of its own -- "%1$s × %2$s" -- is a format
     * pattern rather than prose, and is legitimately identical in both languages.
     */
    private fun isProse(value: String): Boolean =
        value.replace(Regex("""%\d+\$[sd]"""), "").any { it.isLetter() }

    @Test
    fun `no Arabic entry is still holding its English source text`() {
        val untranslatedValues = arabic.filter { (name, value) ->
            name !in untranslated && english[name] == value && isProse(value)
        }.keys

        assertEquals("Arabic entries left as English", emptySet<String>(), untranslatedValues)
    }

    @Test
    fun `format placeholders match between the two languages`() {
        // A translation that drops %1$s throws at runtime rather than at build time.
        val placeholder = Regex("""%\d+\$[sd]""")

        val mismatched = english.filterKeys { it in arabic && it !in untranslated }
            .filter { (name, value) ->
                placeholder.findAll(value).map { it.value }.toSet() !=
                    placeholder.findAll(arabic.getValue(name)).map { it.value }.toSet()
            }.keys

        assertEquals("placeholder mismatch", emptySet<String>(), mismatched)
    }
}
