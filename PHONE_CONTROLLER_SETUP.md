# スマホコントローラー導入

## 1. Unityを更新

[PR #1](https://github.com/Kig15/KosenChanbara/pull/1) の `sanuka` を `main` へマージし、Unityプロジェクトを更新します。使用バージョンはUnity 6000.3.21f1です。

## 2. サーバーを起動

Docker Desktopを起動し、[サーバーのRelease](https://github.com/nyaran2910/KosenChanbaraPhoneController/releases/latest) から `kosen-chanbara-phone-controller-amd64.tar.gz` をUnityプロジェクト直下へ置きます。そこでPowerShellを開いて実行します。

```powershell
docker load -i .\kosen-chanbara-phone-controller-amd64.tar.gz
docker run --rm --name kosen-controller -p 127.0.0.1:8080:8080 --mount "type=bind,source=$($PWD.Path)\Assets\StreamingAssets,target=/unity-config" kosen-chanbara-phone-controller:1.0.0
```

停止は `Ctrl+C` です。起動のたびにUnity用接続設定とQRのURLが自動更新されます。

## 3. ゲームを開始

`BattleGround` を再生してQRを読み、「センサーを使う」を押した後、スマホを指定姿勢にして「リセンター」を押します。PCとスマホが別のWi-FiでもSTUNで直接接続を試みますが、ネットワークによってはTURNサーバーの設定が別途必要です。

Wi-Fiの端末間通信制限を無効にし、Windows FirewallではUnityのプライベートネットワーク通信を許可してください。

サーバーは認証・QR・SDP/ICE接続だけを扱います。姿勢・角速度・加速度・ガード・リセンターはスマホからUnityへ直接送信され、座標変換とゲーム処理はUnity側です。
