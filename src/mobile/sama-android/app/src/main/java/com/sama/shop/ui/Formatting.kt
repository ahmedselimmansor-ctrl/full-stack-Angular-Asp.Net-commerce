package com.sama.shop.ui

import java.text.NumberFormat
import java.util.Currency
import java.util.Locale

/**
 * Formats an amount with its currency.
 *
 * `NumberFormat` knows each currency's minor units — JOD has three decimal places, JPY has
 * none — so hard-coding two would misprint prices in several of the markets Sama serves.
 */
fun formatMoney(amount: Double, currencyCode: String, locale: Locale = Locale.getDefault()): String =
    runCatching {
        NumberFormat.getCurrencyInstance(locale).apply {
            currency = Currency.getInstance(currencyCode)
        }.format(amount)
    }.getOrElse {
        // An unrecognised code should not crash a product grid.
        "%.2f %s".format(amount, currencyCode)
    }

/** Turns the saga's internal state into words a customer understands. */
fun orderStatusLabel(status: String): String = when (status) {
    "Pending" -> "Processing"
    "AwaitingPayment" -> "Taking payment"
    "Confirmed" -> "Confirmed"
    "Shipped" -> "Shipped"
    "Cancelled" -> "Cancelled"
    else -> status
}
