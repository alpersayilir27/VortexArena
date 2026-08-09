package com.vortex.updater

import android.app.Activity
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Gozlukte duran tek ekranli guncelleyici: sabit URL'den oyun APK'sini indirir
 * ve PackageInstaller ile kurar. USB yalniz BU uygulamanin ilk kurulumu icin
 * gerekir; sonrasinda oyun kablosuz guncellenir.
 */
class MainActivity : Activity() {

    companion object {
        private const val GAME_PACKAGE = "com.vortex.arena"
        private const val APK_URL = "http://159.100.20.26:8090/game.apk"

        private const val INSTALL_ACTION = "com.vortex.updater.INSTALL_RESULT"
        private const val REQ_UNINSTALL = 1001
        private const val NET_TIMEOUT_MS = 15000
    }

    private lateinit var statusView: TextView
    private lateinit var versionView: TextView
    private lateinit var progressBar: ProgressBar
    private lateinit var updateButton: Button
    private lateinit var wipeButton: Button
    private lateinit var launchButton: Button

    private var busy = false

    // Kurulum sonucu sistemden broadcast ile gelir; alici dinamik cunku
    // yalnizca uygulama on plandayken anlami var.
    private val installReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            handleInstallResult(intent)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())

        val filter = IntentFilter(INSTALL_ACTION)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            // API 33+ bayragi zorunlu kiliyor; alici yalniz sisteme acik olsun.
            registerReceiver(installReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            registerReceiver(installReceiver, filter)
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        runCatching { unregisterReceiver(installReceiver) }
    }

    override fun onResume() {
        super.onResume()
        refreshInstalledVersion()
    }

    // --- Arayuz (layout XML yok, res/ klasoru hic yok) --------------------

    private fun buildUi(): ViewGroup {
        val pad = (24 * resources.displayMetrics.density).toInt()
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
            setBackgroundColor(Color.BLACK)
        }

        val title = TextView(this).apply {
            text = "VortexArena Guncelleyici"
            textSize = 24f
            setTextColor(Color.WHITE)
        }
        root.addView(title)

        versionView = TextView(this).apply {
            textSize = 18f
            setTextColor(Color.LTGRAY)
            setPadding(0, pad / 2, 0, 0)
        }
        root.addView(versionView)

        statusView = TextView(this).apply {
            text = "Hazir."
            textSize = 18f
            setTextColor(Color.WHITE)
            setPadding(0, pad / 2, 0, pad / 2)
        }
        root.addView(statusView)

        progressBar = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 100
            progress = 0
        }
        root.addView(progressBar)

        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.START
            setPadding(0, pad / 2, 0, 0)
        }
        updateButton = Button(this).apply {
            text = "Guncelle"
            setOnClickListener { onUpdateClicked() }
        }
        wipeButton = Button(this).apply {
            text = "Sil ve Yukle"
            setOnClickListener { onWipeInstallClicked() }
        }
        launchButton = Button(this).apply {
            text = "Oyunu Baslat"
            setOnClickListener { onLaunchClicked() }
        }
        row.addView(updateButton)
        row.addView(wipeButton)
        row.addView(launchButton)
        root.addView(row)

        return root
    }

    private fun setStatus(text: String) {
        runOnUiThread { statusView.text = text }
    }

    private fun setBusy(value: Boolean) {
        runOnUiThread {
            busy = value
            updateButton.isEnabled = !value
            wipeButton.isEnabled = !value
        }
    }

    // --- Kurulu surum -----------------------------------------------------

    private fun getGameVersion(): Pair<String, Long>? = try {
        val info = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            packageManager.getPackageInfo(GAME_PACKAGE, PackageManager.PackageInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION")
            packageManager.getPackageInfo(GAME_PACKAGE, 0)
        }
        Pair(info.versionName ?: "?", info.longVersionCode)
    } catch (e: PackageManager.NameNotFoundException) {
        null
    }

    private fun refreshInstalledVersion() {
        val v = getGameVersion()
        runOnUiThread {
            if (v == null) {
                versionView.text = "Oyun kurulu degil"
                launchButton.isEnabled = false
            } else {
                versionView.text = "Kurulu: ${v.first} (code ${v.second})"
                launchButton.isEnabled = true
            }
        }
    }

    // --- Dugmeler ---------------------------------------------------------

    /** Bilinmeyen kaynak izni yoksa ayara yonlendirir ve false doner. */
    private fun ensureInstallPermission(): Boolean {
        if (packageManager.canRequestPackageInstalls()) return true
        setStatus(
            "Kurulum izni yok. Acilan ayar ekraninda \"Vortex Updater\" icin " +
                "bilinmeyen uygulamalardan kuruluma izin verin, sonra geri donup " +
                "dugmeye tekrar basin."
        )
        startActivity(
            Intent(
                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:$packageName")
            )
        )
        return false
    }

    private fun onUpdateClicked() {
        if (busy) return
        if (!ensureInstallPermission()) return
        downloadAndInstall()
    }

    private fun onWipeInstallClicked() {
        if (busy) return
        if (!ensureInstallPermission()) return

        if (getGameVersion() == null) {
            // Zaten kurulu degil: kaldirma adimina gerek yok.
            downloadAndInstall()
            return
        }

        setStatus("Once mevcut oyun kaldiriliyor...")
        val intent = Intent(Intent.ACTION_UNINSTALL_PACKAGE, Uri.parse("package:$GAME_PACKAGE"))
        intent.putExtra(Intent.EXTRA_RETURN_RESULT, true)
        @Suppress("DEPRECATION")
        startActivityForResult(intent, REQ_UNINSTALL)
    }

    private fun onLaunchClicked() {
        val intent = packageManager.getLaunchIntentForPackage(GAME_PACKAGE)
        if (intent == null) {
            setStatus("Oyun kurulu degil, baslatilamiyor.")
            return
        }
        startActivity(intent)
    }

    @Suppress("DEPRECATION")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != REQ_UNINSTALL) return

        // Sonuc kodu cihazdan cihaza degisiyor; tek guvenilir olcut paketin
        // gercekten gitmis olmasidir.
        if (getGameVersion() != null) {
            setStatus("Kaldirma tamamlanmadi - islem iptal edildi.")
            refreshInstalledVersion()
            return
        }
        refreshInstalledVersion()
        downloadAndInstall()
    }

    // --- Indirme ----------------------------------------------------------

    private fun downloadAndInstall() {
        setBusy(true)
        runOnUiThread { progressBar.progress = 0 }
        setStatus("Indiriliyor: $APK_URL")

        Thread {
            val target = File(cacheDir, "game.apk")
            val error = downloadTo(target)
            if (error != null) {
                setStatus(error)
                setBusy(false)
                return@Thread
            }
            setStatus("Indirme tamam (${target.length() / (1024 * 1024)} MB). Kurulum baslatiliyor...")
            installApk(target)
        }.start()
    }

    /** Basarili ise null, degilse kullaniciya gosterilecek hata metni doner. */
    private fun downloadTo(target: File): String? {
        var connection: HttpURLConnection? = null
        try {
            connection = (URL(APK_URL).openConnection() as HttpURLConnection).apply {
                connectTimeout = NET_TIMEOUT_MS
                readTimeout = NET_TIMEOUT_MS
                requestMethod = "GET"
            }
            connection.connect()

            val code = connection.responseCode
            if (code != HttpURLConnection.HTTP_OK) {
                return when (code) {
                    404 -> "HTTP 404 - sunucuda game.apk yok ya da IIS'te .apk MIME eslemesi eksik."
                    403 -> "HTTP 403 - IIS dosyaya erisim izni vermiyor."
                    else -> "Sunucu HTTP $code dondu."
                }
            }

            val total = connection.contentLength.toLong()
            connection.inputStream.use { input ->
                target.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var written = 0L
                    var lastPercent = -1
                    while (true) {
                        val read = input.read(buffer)
                        if (read <= 0) break
                        output.write(buffer, 0, read)
                        written += read
                        if (total > 0) {
                            val percent = ((written * 100) / total).toInt()
                            if (percent != lastPercent) {
                                lastPercent = percent
                                runOnUiThread { progressBar.progress = percent }
                            }
                        }
                    }
                }
            }
            if (target.length() <= 0L) return "Indirilen dosya bos - sunucudaki game.apk bozuk olabilir."
            return null
        } catch (e: IOException) {
            return "Sunucuya ulasilamadi: ${e.message}"
        } catch (e: Exception) {
            return "Indirme hatasi: ${e.message}"
        } finally {
            connection?.disconnect()
        }
    }

    // --- Kurulum ----------------------------------------------------------

    private fun installApk(apk: File) {
        try {
            val installer = packageManager.packageInstaller
            val params = PackageInstaller.SessionParams(
                PackageInstaller.SessionParams.MODE_FULL_INSTALL
            )
            params.setAppPackageName(GAME_PACKAGE)

            val sessionId = installer.createSession(params)
            installer.openSession(sessionId).use { session ->
                session.openWrite("game.apk", 0, apk.length()).use { output ->
                    apk.inputStream().use { input -> input.copyTo(output) }
                    session.fsync(output)
                }

                // PendingIntent MUTABLE olmali: sistem EXTRA_STATUS'u icine yaziyor.
                // setPackage sart - Android 14 hedefi belirsiz mutable intent'i reddeder.
                val intent = Intent(INSTALL_ACTION).setPackage(packageName)
                val pending = PendingIntent.getBroadcast(
                    this,
                    sessionId,
                    intent,
                    PendingIntent.FLAG_MUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
                )
                session.commit(pending.intentSender)
            }
            setStatus("Kurulum onayi bekleniyor...")
        } catch (e: Exception) {
            setStatus("Kurulum baslatilamadi: ${e.message}")
            setBusy(false)
        }
    }

    private fun handleInstallResult(intent: Intent) {
        when (val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, Int.MIN_VALUE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                val confirm = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
                } else {
                    @Suppress("DEPRECATION")
                    intent.getParcelableExtra<Intent>(Intent.EXTRA_INTENT)
                }
                if (confirm == null) {
                    setStatus("Sistem onay ekranini gonderemedi.")
                    setBusy(false)
                    return
                }
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                startActivity(confirm)
            }

            PackageInstaller.STATUS_SUCCESS -> {
                setStatus("Kurulum tamamlandi.")
                setBusy(false)
                refreshInstalledVersion()
            }

            else -> {
                val message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)
                setStatus("Kurulum basarisiz (durum $status): ${message ?: "ayrinti yok"}")
                setBusy(false)
                refreshInstalledVersion()
            }
        }
    }
}
