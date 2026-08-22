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
import android.widget.ScrollView
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Single-screen updater running on the headset: lists every game version published
 * on the server and installs the selected one. Each version is a separate package
 * ("com.vortex.arenav<version>"), so they can live side by side on the device.
 */
class MainActivity : Activity() {

    companion object {
        // Server addresses live only here; there is no settings screen on the headset.
        private const val GAME_PACKAGE_PREFIX = "com.vortex.arenav"
        private const val VERSIONS_URL = "http://159.100.20.26:8091/versions"
        private const val DOWNLOAD_BASE = "http://159.100.20.26:8090/game_versions/"

        private const val INSTALL_ACTION = "com.vortex.updater.INSTALL_RESULT"
        private const val REQ_UNINSTALL_FOR_RETRY = 1002
        private const val NET_TIMEOUT_MS = 15000
    }

    /** One row of the server listing; size is 0 when the server omitted it. */
    private data class VersionEntry(val version: Int, val size: Long)

    private lateinit var statusView: TextView
    private lateinit var progressBar: ProgressBar
    private lateinit var refreshButton: Button
    private lateinit var listContainer: LinearLayout

    private var busy = false

    // Last listing fetched from the server. onResume redraws from this instead of
    // hitting the network, so a row flips to "Ac" right after the install dialog.
    private var versions: List<VersionEntry> = emptyList()
    private var listLoaded = false

    // Downloaded APK kept around: on a signature conflict the package is removed
    // and the very same file is installed again without re-downloading ~600 MB.
    private var pendingApk: File? = null
    private var pendingVersion: Int = -1

    // Install result arrives as a broadcast; receiver is dynamic because it only
    // means anything while the app is in the foreground.
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
            // API 33+ requires the flag; keep the receiver system-only.
            registerReceiver(installReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            registerReceiver(installReceiver, filter)
        }

        fetchVersions()
    }

    override fun onDestroy() {
        super.onDestroy()
        runCatching { unregisterReceiver(installReceiver) }
    }

    override fun onResume() {
        super.onResume()
        // No network here: only the installed/not-installed state can have changed.
        if (listLoaded) renderVersions()
    }

    // --- UI (no layout XML, no res/ folder at all) ------------------------

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

        statusView = TextView(this).apply {
            text = "Surumler aliniyor..."
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
            setPadding(0, pad / 2, 0, pad / 2)
        }
        refreshButton = Button(this).apply {
            text = "Yenile"
            setOnClickListener { onRefreshClicked() }
        }
        row.addView(refreshButton)
        root.addView(row)

        listContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }
        val scroll = ScrollView(this).apply {
            addView(listContainer)
        }
        root.addView(
            scroll,
            LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                0
            ).apply { weight = 1f }
        )

        return root
    }

    private fun setStatus(text: String) {
        runOnUiThread { statusView.text = text }
    }

    private fun setBusy(value: Boolean) {
        runOnUiThread {
            busy = value
            refreshButton.isEnabled = !value
            for (i in 0 until listContainer.childCount) {
                setRowEnabled(listContainer.getChildAt(i), !value)
            }
        }
    }

    private fun setRowEnabled(view: android.view.View, enabled: Boolean) {
        if (view !is ViewGroup) return
        for (i in 0 until view.childCount) {
            val child = view.getChildAt(i)
            if (child is Button) child.isEnabled = enabled
        }
    }

    // --- Version list -----------------------------------------------------

    private fun packageOf(version: Int) = "$GAME_PACKAGE_PREFIX$version"

    private fun isInstalled(version: Int): Boolean = try {
        val pkg = packageOf(version)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            packageManager.getPackageInfo(pkg, PackageManager.PackageInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION")
            packageManager.getPackageInfo(pkg, 0)
        }
        true
    } catch (e: PackageManager.NameNotFoundException) {
        false
    }

    private fun onRefreshClicked() {
        if (busy) return
        fetchVersions()
    }

    private fun fetchVersions() {
        setBusy(true)
        setStatus("Surumler aliniyor...")
        Thread {
            val result = loadVersions()
            val error = result.second
            runOnUiThread {
                if (error != null) {
                    statusView.text = error
                } else {
                    versions = result.first
                    listLoaded = true
                    statusView.text = if (versions.isEmpty()) {
                        "Sunucuda hic surum yok."
                    } else {
                        "${versions.size} surum bulundu."
                    }
                    renderVersions()
                }
                setBusy(false)
            }
        }.start()
    }

    /** Returns (list, null) on success, (empty, message) on failure. */
    private fun loadVersions(): Pair<List<VersionEntry>, String?> {
        var connection: HttpURLConnection? = null
        try {
            connection = (URL(VERSIONS_URL).openConnection() as HttpURLConnection).apply {
                connectTimeout = NET_TIMEOUT_MS
                readTimeout = NET_TIMEOUT_MS
                requestMethod = "GET"
            }
            connection.connect()

            val code = connection.responseCode
            if (code != HttpURLConnection.HTTP_OK) {
                return Pair(emptyList(), "Sunucu HTTP $code dondu.")
            }

            val body = connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            val array: JSONArray = JSONObject(body).optJSONArray("versions")
                ?: return Pair(emptyList(), "Sunucu listesi okunamadi - beklenen alan yok.")

            // Server already sorts newest first; keep its order.
            val list = ArrayList<VersionEntry>(array.length())
            for (i in 0 until array.length()) {
                val item = array.optJSONObject(i) ?: continue
                val version = item.optInt("version", -1)
                if (version <= 0) continue
                list.add(VersionEntry(version, item.optLong("size", 0L)))
            }
            return Pair(list, null)
        } catch (e: IOException) {
            return Pair(emptyList(), "Sunucuya ulasilamadi: ${e.message}")
        } catch (e: Exception) {
            return Pair(emptyList(), "Surum listesi cozumlenemedi: ${e.message}")
        } finally {
            connection?.disconnect()
        }
    }

    private fun renderVersions() {
        val pad = (12 * resources.displayMetrics.density).toInt()
        listContainer.removeAllViews()

        for (entry in versions) {
            val installed = isInstalled(entry.version)

            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(0, pad / 2, 0, pad / 2)
            }

            val label = TextView(this).apply {
                text = buildString {
                    append("Surum ${entry.version}")
                    if (entry.size > 0) append("  ·  ${entry.size / (1024 * 1024)} MB")
                    if (installed) append("  ·  kurulu")
                }
                textSize = 18f
                setTextColor(Color.WHITE)
            }
            row.addView(
                label,
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT).apply { weight = 1f }
            )

            val action = Button(this).apply {
                text = if (installed) "Ac" else "Indir"
                isEnabled = !busy
                setOnClickListener {
                    if (installed) onLaunchClicked(entry.version) else onDownloadClicked(entry.version)
                }
            }
            row.addView(action)

            listContainer.addView(
                row,
                LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                )
            )
        }
    }

    // --- Buttons ----------------------------------------------------------

    /** Sends the user to the settings screen and returns false when the install permission is missing. */
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

    private fun onLaunchClicked(version: Int) {
        if (busy) return
        val intent = packageManager.getLaunchIntentForPackage(packageOf(version))
        if (intent == null) {
            setStatus("Surum $version kurulu degil, baslatilamiyor.")
            renderVersions()
            return
        }
        startActivity(intent)
    }

    private fun onDownloadClicked(version: Int) {
        if (busy) return
        if (!ensureInstallPermission()) return
        downloadAndInstall(version)
    }

    @Suppress("DEPRECATION")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != REQ_UNINSTALL_FOR_RETRY) return

        val version = pendingVersion
        // Result code varies between devices; the only reliable check is whether
        // the package is really gone.
        if (version <= 0 || isInstalled(version)) {
            setStatus("Kaldirma tamamlanmadi - islem iptal edildi.")
            setBusy(false)
            renderVersions()
            return
        }

        val apk = pendingApk
        if (apk == null || !apk.exists()) {
            setStatus("Eski surum kaldirildi ama indirilen dosya bulunamadi - Indir'e tekrar basin.")
            setBusy(false)
            renderVersions()
            return
        }
        setStatus("Eski surum kaldirildi. Tekrar kuruluyor...")
        installApk(apk, version)
    }

    // --- Download ---------------------------------------------------------

    private fun downloadAndInstall(version: Int) {
        setBusy(true)
        runOnUiThread { progressBar.progress = 0 }
        setStatus("Indiriliyor: surum $version")

        Thread {
            // Each build is ~600 MB; leftover downloads would fill the headset.
            clearCachedApks()

            val target = File(cacheDir, "game_v$version.apk")
            val error = downloadTo(version, target)
            if (error != null) {
                setStatus(error)
                setBusy(false)
                return@Thread
            }
            setStatus("Indirme tamam (${target.length() / (1024 * 1024)} MB). Kurulum baslatiliyor...")
            pendingApk = target
            pendingVersion = version
            installApk(target, version)
        }.start()
    }

    private fun clearCachedApks() {
        val files = cacheDir.listFiles() ?: return
        for (file in files) {
            if (file.isFile && file.name.startsWith("game_v") && file.name.endsWith(".apk")) {
                runCatching { file.delete() }
            }
        }
    }

    /** Returns null on success, otherwise the message shown to the user. */
    private fun downloadTo(version: Int, target: File): String? {
        var connection: HttpURLConnection? = null
        val url = "${DOWNLOAD_BASE}game_v$version.apk"
        try {
            connection = (URL(url).openConnection() as HttpURLConnection).apply {
                connectTimeout = NET_TIMEOUT_MS
                readTimeout = NET_TIMEOUT_MS
                requestMethod = "GET"
            }
            connection.connect()

            val code = connection.responseCode
            if (code != HttpURLConnection.HTTP_OK) {
                return when (code) {
                    404 -> "HTTP 404 - sunucuda game_v$version.apk yok ya da IIS'te .apk MIME eslemesi eksik."
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
            if (target.length() <= 0L) return "Indirilen dosya bos - sunucudaki game_v$version.apk bozuk olabilir."
            return null
        } catch (e: IOException) {
            return "Sunucuya ulasilamadi: ${e.message}"
        } catch (e: Exception) {
            return "Indirme hatasi: ${e.message}"
        } finally {
            connection?.disconnect()
        }
    }

    // --- Install ----------------------------------------------------------

    private fun installApk(apk: File, version: Int) {
        try {
            val installer = packageManager.packageInstaller
            val params = PackageInstaller.SessionParams(
                PackageInstaller.SessionParams.MODE_FULL_INSTALL
            )
            params.setAppPackageName(packageOf(version))

            val sessionId = installer.createSession(params)
            installer.openSession(sessionId).use { session ->
                session.openWrite(apk.name, 0, apk.length()).use { output ->
                    apk.inputStream().use { input -> input.copyTo(output) }
                    session.fsync(output)
                }

                // PendingIntent must be MUTABLE: the system writes EXTRA_STATUS into it.
                // setPackage is required - Android 14 rejects mutable implicit intents.
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
                runOnUiThread { renderVersions() }
            }

            else -> {
                val message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)

                // Installed package carries a different signature (e.g. the dev/CI key
                // changed): the system returns STATUS_FAILURE_CONFLICT, usually with
                // "INSTALL_FAILED_UPDATE_INCOMPATIBLE". Remove it and retry with the
                // already downloaded file.
                val isSignatureMismatch = status == PackageInstaller.STATUS_FAILURE_CONFLICT ||
                    (message?.contains("INCOMPATIBLE", ignoreCase = true) == true) ||
                    (message?.contains("signatures do not match", ignoreCase = true) == true)

                val version = pendingVersion
                if (isSignatureMismatch && version > 0 && isInstalled(version)) {
                    setStatus("Kurulu surumun imzasi farkli - eski surum kaldirilip tekrar kuruluyor...")
                    val uninstall = Intent(
                        Intent.ACTION_UNINSTALL_PACKAGE,
                        Uri.parse("package:${packageOf(version)}")
                    )
                    uninstall.putExtra(Intent.EXTRA_RETURN_RESULT, true)
                    @Suppress("DEPRECATION")
                    startActivityForResult(uninstall, REQ_UNINSTALL_FOR_RETRY)
                    return
                }

                setStatus("Kurulum basarisiz (durum $status): ${message ?: "ayrinti yok"}")
                setBusy(false)
                runOnUiThread { renderVersions() }
            }
        }
    }
}
