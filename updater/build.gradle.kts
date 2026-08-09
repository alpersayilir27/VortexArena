// AGP surumu Unity 6000.3'un gomulu Gradle'ina (9.1.0) gore secildi; eski
// 8.x AGP'ler Gradle 9 altinda test edilmemistir. Unity yukseltilir de gomulu
// Gradle degisirse once buradaki uyumluluga bak.
plugins {
    id("com.android.application") version "8.13.0" apply false
    id("org.jetbrains.kotlin.android") version "1.9.24" apply false
}
