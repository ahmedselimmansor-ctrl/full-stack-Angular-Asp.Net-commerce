package com.sama.shop.ui

import java.text.NumberFormat
import java.util.Currency
import java.util.Locale

/**
 * Formats an amount with its currency.
 *
 * Note the explicit fraction digits. Unlike the browser's `Intl.NumberFormat`, Java's
 * `NumberFormat.setCurrency` deliberately does *not* adjust the fraction digits — the
 * documentation says as much — so a formatter built for a two-decimal locale keeps printing
 * two decimals after being handed a three-decimal currency. Left alone, JOD and KWD prices
 * would silently lose their third decimal place, and JPY would gain two it does not have.
 */
fun formatMoney(amount: Double, currencyCode: String, locale: Locale = Locale.getDefault()): String =
    runCatching {
        val currency = Currency.getInstance(currencyCode)
        val digits = currency.defaultFractionDigits.coerceAtLeast(0)

        NumberFormat.getCurrencyInstance(locale).apply {
            this.currency = currency
            minimumFractionDigits = digits
            maximumFractionDigits = digits
        }.format(amount)
    }.getOrElse {
        // An unrecognised code should not take down a product grid; show the number and the
        // code so the price is still readable.
        "%.2f %s".format(locale, amount, currencyCode)
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
