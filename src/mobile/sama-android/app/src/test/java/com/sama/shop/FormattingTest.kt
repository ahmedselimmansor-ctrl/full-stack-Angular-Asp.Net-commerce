package com.sama.shop

import com.sama.shop.ui.formatMoney
import com.sama.shop.ui.orderStatusLabel
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Locale

class FormattingTest {

    @Test
    fun `formats an amount with its currency symbol`() {
        assertEquals("$19.99", formatMoney(19.99, "USD", Locale.US))
    }

    @Test
    fun `respects each currency's minor units`() {
        // JOD has three decimal places and JPY has none. Hard-coding two would misprint
        // prices in several of the markets Sama serves.
        assertTrue(formatMoney(1.5, "JOD", Locale.US).contains("1.500"))
        assertTrue(!formatMoney(1500.0, "JPY", Locale.US).contains("."))
    }

    @Test
    fun `falls back rather than crashing on an unknown currency code`() {
        // A product grid must not be taken down by one unrecognised code arriving from a
        // server that is ahead of this build.
        val formatted = formatMoney(10.0, "XYZ", Locale.US)

        assertTrue(formatted.contains("10"))
        assertTrue(formatted.contains("XYZ"))
    }

    @Test
    fun `translates the saga's internal states into customer wording`() {
        // "AwaitingPayment" describes a saga step accurately and confuses a shopper.
        assertEquals("Taking payment", orderStatusLabel("AwaitingPayment"))
        assertEquals("Processing", orderStatusLabel("Pending"))
        assertEquals("Confirmed", orderStatusLabel("Confirmed"))
    }

    @Test
    fun `passes an unrecognised status through rather than hiding it`() {
        // Showing nothing would make a newly-added server-side state look like a bug in the app.
        assertEquals("Refunding", orderStatusLabel("Refunding"))
    }
}
