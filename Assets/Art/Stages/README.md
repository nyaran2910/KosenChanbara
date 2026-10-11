# ゲームのステージ

- 通常の対戦: `ChambaraStage/ChambaraStage.prefab`。
- 対戦のサドンデス: `ChambaraStageSmall/ChambaraStageSmall.prefab`。通常の台と同じモデル縮尺で取り込み、供給された小さい台の縁に合わせて場外半径を通常の 0.75 倍にする。
- シングル・フリー: `CircularStadium/CircularStadium.prefab`。対戦でもこのスタジアムを台の背景として使用する。

FBX と JSON は、それぞれ指定された `chambara_stage.blend`、`chambara_stage_small.blend`、`circular_stadium.blend` から Blender 5.2 LTS で生成した。JSON に元ファイルの SHA-256 と材質情報を記録している。実行時に Downloads フォルダーや Blender は不要。

台は床、縁、ロゴ、梁、支柱、ボルトを保持し、撮影用の `Studio_ground` だけを除外する。通常・小さい台は各 73 メッシュ、6 材質。床の上面はゲーム座標の Y=0 に合わせる。

スタジアムは元の 1,414 メッシュの形状を保持し、客席、屋根、ゴール、照明を材質と方角ごとにまとめて 134 メッシュ、24 材質にする。円形の床 `Infield_concrete_foundation` を独立して保持し、その水平 bounds からシングル・フリーの移動外周を取得する（供給モデルは半径83）。芝の 14 本の縞は個別に保持し、芝の上面を Y=0 に合わせる。Blender のプロシージャルな凹凸は URP Lit の基本色・粗さ・金属度で近似し、透明度と発光色も取り込む。

再生成は Unity プロジェクトのルートで以下を実行する。`BLENDER`、`UNITY` はインストール先の実行ファイルを指定する。

```sh
"$BLENDER" --background --factory-startup --disable-autoexec --python Assets/Editor/Chambara/ExportChambaraStage.py -- /path/to/chambara_stage_small.blend Assets/Art/Stages/ChambaraStageSmall/ChambaraStageSmall.fbx
"$BLENDER" --background --factory-startup --disable-autoexec --python Assets/Editor/Chambara/ExportCircularStadium.py -- /path/to/circular_stadium.blend Assets/Art/Stages/CircularStadium/CircularStadium.fbx
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod ChambaraStageAssets.BuildAdditionalStages -logFile /tmp/chambara-stage-build.log
```

最後の処理は既存の通常ステージを基準に Prefab と材質を作り、BattleGround シーンのステージ参照とサドンデス半径比を保存する。編集中の Unity とは別のプロジェクトコピーで実行する。
