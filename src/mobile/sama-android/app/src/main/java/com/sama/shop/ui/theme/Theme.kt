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

// The same palette the web storefront uses, so the two feel like one product.
private val SamaBrown = Color(0xFF8A5A2B)
private val SamaBrownLight = Color(0xFFC98D54)
private val SamaSand = Color(0xFFF3E9DF)
private val SamaInk = Color(0xFF1C1A17)
private val SamaPaper = Color(0xFFFBFAF8)

private val LightColors = lightColorScheme(
    primary = SamaBrown,
    onPrimary = Color.White,
    primaryContainer = SamaSand,
    onPrimaryContainer = SamaInk,
    background = SamaPaper,
    onBackground = SamaInk,
    surface = Color.White,
    onSurface = SamaInk,
)

private val DarkColors = darkColorScheme(
    primary = SamaBrownLight,
    onPrimary = Color(0xFF171614),
    primaryContainer = Color(0xFF2F2721),
    onPrimaryContainer = Color(0xFFF2EFE9),
    background = Color(0xFF171614),
    onBackground = Color(0xFFF2EFE9),
    surface = Color(0xFF201E1B),
    onSurface = Color(0xFFF2EFE9),
)

@Composable
fun SamaTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    // Material You wallpaper colours, where the platform supports them. Brand colours are the
    // fallback rather than the override: a user who has themed their device expects apps to
    // follow it.
    dynamicColor: Boolean = true,
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
