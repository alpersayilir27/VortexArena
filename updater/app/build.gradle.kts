plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.vortex.updater"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.vortex.updater"
        minSdk = 29
        targetSdk = 34
        versionCode = 1
        versionName = "1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}
// src/main/kotlin ayrica tanimlanmaz: kotlin-android eklentisi onu zaten kaynak
// kokune ekliyor, ikinci tanim sessizce sapabilecek bir yol daha olurdu.

// Bagimlilik YOK (androidx/appcompat dahil): arayuz saf android.app.Activity ile
// koda yaziliyor. Sifir bagimlilik = ilk kosuda inecek tek sey AGP'nin kendisi.
