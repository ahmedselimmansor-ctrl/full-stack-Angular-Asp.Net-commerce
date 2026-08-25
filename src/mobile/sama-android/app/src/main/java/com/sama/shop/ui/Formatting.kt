package com.sama.shop.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import com.sama.shop.R

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
/**
 * Maps a saga status onto the string resource that describes it in language a
 * customer understands.
 *
 * Returns a resource id rather than finished text so the label follows the
 * device language; the previous version hard-coded English, which meant an
 * Arabic device showed "Taking payment" in the middle of an Arabic screen.
 *
 * An unrecognised status has no resource and is surfaced verbatim by the caller:
 * showing the wire value is honest, whereas a blank chip would hide that the API
 * has a state this build does not know about.
 */
fun orderStatusLabelRes(status: String): Int? = when (status) {
    "Pending" -> R.string.status_pending
    "AwaitingPayment" -> R.string.status_awaiting_payment
    "Confirmed" -> R.string.status_confirmed
    "Shipped" -> R.string.status_shipped
    "Cancelled" -> R.string.status_cancelled
    else -> null
}

@Composable
fun orderStatusLabel(status: String): String =
    orderStatusLabelRes(status)?.let { stringResource(it) } ?: status
