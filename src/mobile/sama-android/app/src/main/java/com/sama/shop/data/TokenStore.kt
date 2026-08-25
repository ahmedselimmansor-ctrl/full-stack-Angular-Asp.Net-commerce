package com.sama.shop.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import dagger.hilt.android.qualifiers.ApplicationContext
import java.util.concurrent.atomic.AtomicReference
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Holds the session's credentials.
 *
 * The two tokens are stored very differently on purpose:
 *
 *  - the **access token** lives only in memory. It expires in minutes, and keeping it off disk
 *    means a device backup, a forensic image or a rooted-device dump yields nothing usable.
 *  - the **refresh token** must survive the app being killed, so it is written to
 *    `EncryptedSharedPreferences`, encrypted with a key held in the Android Keystore. On a
 *    device with a hardware-backed keystore that key cannot be extracted at all, so copying
 *    the preferences file to another device produces an unreadable blob.
 */
@Singleton
class TokenStore @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val preferences: SharedPreferences by lazy {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            context,
            PREFERENCES_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    // Atomic because the OkHttp authenticator reads and replaces this from a background thread
    // while the UI reads it from the main one.
    private val accessToken = AtomicReference<String?>(null)

    fun accessToken(): String? = accessToken.get()

    fun refreshToken(): String? = preferences.getString(KEY_REFRESH, null)

    fun hasSession(): Boolean = refreshToken() != null

    fun save(tokens: TokenResponse) {
        accessToken.set(tokens.accessToken)
        preferences.edit().putString(KEY_REFRESH, tokens.refreshToken).apply()
    }

    fun updateAccessToken(token: String) {
        accessToken.set(token)
    }

    fun clear() {
        accessToken.set(null)

        // commit(), not apply(): sign-out must be durable before the caller continues. An
        // asynchronous write could be lost if the process dies immediately afterwards,
        // leaving a revoked session apparently still valid on next launch.
        preferences.edit().remove(KEY_REFRESH).commit()
    }

    private companion object {
        const val PREFERENCES_NAME = "sama_session"
        const val KEY_REFRESH = "refresh_token"
    }
}
