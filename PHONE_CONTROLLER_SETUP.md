# スマホコントローラー導入

## 1. Unityを更新

[PR #1](https://github.com/Kig15/KosenChanbara/pull/1) の `sanuka` を `main` へマージし、Unityプロジェクトを更新します。使用バージョンはUnity 6000.3.21f1です。

## 2. サーバーを起動

Windows親機では総合リポジトリのルートで `just server` を実行します。Git・Node.js 24以上・justが必要です。学校Wi-Fi→有線LANの共有と、phone・HTTPSトンネル・score-serverを起動します。ルータのAP/ブリッジ化とDHCPオフは一度手動で行います。詳細は総合リポジトリの `docs/EVENT_SERVER_SETUP.md` を参照してください。

Mac/Linux開発時は `just phone-start` / `just phone-stop` を使います。UnityはUDP42045でphoneを自動探索し、IP・キーの配布は不要です。一時HTTPS URLの利用中はスマホもインターネット接続が必要です。

## 3. ゲームを開始

`BattleGround` を再生し、Esc → コントローラ → P1/P2から表示されるQRを読み、「センサーを使う」を押した後、スマホを指定姿勢にして「リセンター」を押します。接続後はUnityで軸の組み合わせ6通りと各軸の正転・反転を選び、「完了」で保存してEscへ戻ります。PCとスマホが別のWi-FiでもSTUNで直接接続を試みますが、ネットワークによってはTURNサーバーの設定が別途必要です。

Wi-Fiの端末間通信制限を無効にし、Windows FirewallではUnityのプライベートネットワーク通信を許可してください。

サーバーは認証・QR・SDP/ICE接続だけを扱います。姿勢・角速度・加速度・ガード・リセンターはスマホからUnityへ直接送信され、座標変換とゲーム処理はUnity側です。
