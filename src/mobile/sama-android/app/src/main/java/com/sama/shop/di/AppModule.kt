package com.sama.shop.di

import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import com.sama.shop.BuildConfig
import com.sama.shop.data.AuthHeaderInterceptor
import com.sama.shop.data.SamaApi
import com.sama.shop.data.TokenRefreshAuthenticator
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import java.util.concurrent.TimeUnit
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides
    @Singleton
    fun json(): Json = Json {
        // An installed app cannot be updated in step with the backend, so a field added
        // server-side must not break clients that are months behind.
        ignoreUnknownKeys = true

        // Absent optional fields fall back to the Kotlin default rather than throwing.
        explicitNulls = false
        coerceInputValues = true
    }

    @Provides
    @Singleton
    fun okHttpClient(
        authHeader: AuthHeaderInterceptor,
        authenticator: TokenRefreshAuthenticator,
    ): OkHttpClient = OkHttpClient.Builder()
        .addInterceptor(authHeader)
        .authenticator(authenticator)
        .apply {
            if (BuildConfig.DEBUG) {
                // BASIC, not BODY. Request bodies here carry passwords and session tokens,
                // and logcat is readable by anyone with the device plugged in.
                addInterceptor(
                    HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC },
                )
            }
        }
        // Mobile networks are slow and lossy; these are generous but bounded, so a request
        // cannot hang a screen indefinitely.
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        // Off: retrying is safe for reads but not for a checkout, and OkHttp cannot tell them
        // apart. Idempotent retries are the repository's decision, made with the
        // Idempotency-Key that makes them safe.
        .retryOnConnectionFailure(false)
        .build()

    @Provides
    @Singleton
    fun retrofit(client: OkHttpClient, json: Json): Retrofit = Retrofit.Builder()
        .baseUrl(BuildConfig.API_BASE_URL)
        .client(client)
        .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
        .build()

    @Provides
    @Singleton
    fun samaApi(retrofit: Retrofit): SamaApi = retrofit.create(SamaApi::class.java)
}
