# kotlinx.serialization generates serializers reflectively by name, so R8 must not rename or
# strip them — without these rules a release build fails to deserialise every API response
# while the debug build works perfectly.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**

-keepclassmembers class kotlinx.serialization.json.** {
    *** Companion;
}
-keepclasseswithmembers class kotlinx.serialization.json.** {
    kotlinx.serialization.KSerializer serializer(...);
}

-keep,includedescriptorclasses class com.sama.shop.data.**$$serializer { *; }
-keepclassmembers class com.sama.shop.data.** {
    *** Companion;
}
-keepclasseswithmembers class com.sama.shop.data.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# Retrofit reads generic signatures from interface methods at runtime.
-keepattributes Signature, Exceptions
-keep,allowobfuscation,allowshrinking interface retrofit2.Call
-keep,allowobfuscation,allowshrinking class kotlin.coroutines.Continuation

# Tink (via androidx.security-crypto, which encrypts the stored refresh token) is annotated
# with Error Prone annotations. Those are compile-time only and are deliberately not shipped
# as a runtime dependency, so R8 sees dangling references and refuses to complete.
#
# Suppressing the warning is correct rather than a workaround: an annotation that is not
# retained at runtime cannot affect behaviour, and adding the artifact purely to satisfy R8
# would bloat the APK for nothing.
-dontwarn com.google.errorprone.annotations.**

# Same shape: OkHttp optionally integrates with Conscrypt, BouncyCastle and OpenJSSE when they
# are present. None are bundled, so the references are expected to be absent at runtime.
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**
