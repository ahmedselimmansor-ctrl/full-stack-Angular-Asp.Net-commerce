package com.sama.shop.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext

// The same palette the web storefront uses, so the two read as one product.
//
// Values are lifted from src/web/sama-web/src/styles.scss and verified against
// WCAG AA there by scripts/check-contrast.py. The dusk field is constant across
// both themes -- the identity does not invert when someone flips the lights --
// while the glaze accent deepens for light and brightens for dark, because a
// single mid-tone fails contrast in one direction or the other.
private val SamaField = Color(0xFF0B1D28)       // dusk brand ground, both themes
private val SamaGlaze = Color(0xFF0C776E)       // ceramic turquoise, light theme
private val SamaGlazeBright = Color(0xFF2FD4C6) // ceramic turquoise, dark theme
private val SamaGlazeWash = Color(0xFFDCEFEC)
private val SamaSodium = Color(0xFFB8761A)      // street light
private val SamaSodiumBright = Color(0xFFE9A13B)

// Light: pale plaster, deliberately cooler than cream.
private val LightBackground = Color(0xFFEBEEEC)
private val LightSurface = Color(0xFFF7F9F7)
private val LightText = Color(0xFF0E1A1F)
private val LightTextSoft = Color(0xFF425258)
private val LightLine = Color(0xFFD3DAD6)
private val LightCritical = Color(0xFFA72F2F)

private val DarkBackground = Color(0xFF08131A)
private val DarkSurface = Color(0xFF0F212B)
private val DarkText = Color(0xFFE6EEF0)
private val DarkTextSoft = Color(0xFF9DB3BB)
private val DarkLine = Color(0xFF1E3A47)
private val DarkCritical = Color(0xFFF08C8C)

private val LightColors = lightColorScheme(
    primary = SamaGlaze,
    onPrimary = Color.White,
    primaryContainer = SamaGlazeWash,
    onPrimaryContainer = SamaGlaze,
    secondary = SamaSodium,
    onSecondary = Color.White,
    background = LightBackground,
    onBackground = LightText,
    surface = LightSurface,
    onSurface = LightText,
    surfaceVariant = Color(0xFFEEF2F0),
    onSurfaceVariant = LightTextSoft,
    outline = Color(0xFF838A87),
    outlineVariant = LightLine,
    error = LightCritical,
    onError = Color.White,
    // The dusk field, for the masthead and any surface carrying the brand.
    inverseSurface = SamaField,
    inverseOnSurface = Color(0xFFE8F1F0),
)

private val DarkColors = darkColorScheme(
    primary = SamaGlazeBright,
    onPrimary = Color(0xFF052620),
    primaryContainer = Color(0xFF103334),
    onPrimaryContainer = SamaGlazeBright,
    secondary = SamaSodiumBright,
    onSecondary = Color(0xFF33260F),
    background = DarkBackground,
    onBackground = DarkText,
    surface = DarkSurface,
    onSurface = DarkText,
    surfaceVariant = Color(0xFF142B36),
    onSurfaceVariant = DarkTextSoft,
    outline = Color(0xFF5A7682),
    outlineVariant = DarkLine,
    error = DarkCritical,
    onError = Color(0xFF351B1B),
    inverseSurface = SamaField,
    inverseOnSurface = Color(0xFFE8F1F0),
)

@Composable
fun SamaTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    // Off by default, which is the opposite of the usual Material You advice and is
    // a deliberate trade.
    //
    // Sama's identity is the dusk field and the ceramic glaze, and it is the same
    // identity on the web, where there is no wallpaper to follow. Deriving the
    // scheme from the device would make the app the one surface that does not
    // look like the product, and would discard a palette whose every rendered
    // pairing is checked against WCAG AA in CI.
    //
    // Still a parameter rather than a hard-coded false, so a host that would
    // rather follow the device can opt in.
    dynamicColor: Boolean = false,
    content: @Composable () -> Unit,
) {
    val colors = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            val context = LocalContext.current
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        }
        darkTheme -> DarkColors
        else -> LightColors
    }

    MaterialTheme(colorScheme = colors, content = content)
}
