# Changelog

## [2.2.1](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v2.2.0...v2.2.1) (2026-02-17)


### 🐛 Bug Fixes

* **python_server:** use standalone build to prevent AV issues ([#76](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/76)) ([50b861d](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/50b861d91f37f46a78eee10fc34bcb70ebb651b8))


### 📦 Dependency Updates

* bump liquidctl/liquidctl to f9f0025 ([#82](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/82)) ([be457a5](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/be457a5af60e323f895c07185eea7a3cadfd778e))

## [2.2.0](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v2.1.0...v2.2.0) (2026-02-08)


### 🎉 New Features

* **python-serverr:** build with MSVC and specify tempdir to reduce risque of AV false positive ([#71](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/71)) ([52737c5](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/52737c5039c975ed22bde07344af839997775270))

## [2.1.0](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v2.0.0...v2.1.0) (2026-02-03)


### 🎉 New Features

* **python server:** use liquidctl github version to support newer devices ([#65](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/65)) ([fcc0bdf](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/fcc0bdfbbca4faef2483c2d4b320119f802f8067))


### 📚 Documentation

* update dev requirements in readme ([96162fa](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/96162faa9a65c6cf5067af4c0e989e419a28600a))

## [2.0.0](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v1.1.0...v2.0.0) (2026-01-19)


### ⚠ BREAKING CHANGES

* The key of the controllers have been updated, so the devices will need to be reconfigured

### 🎉 New Features

* rewrite python server and c# client for better performance and robustness ([#46](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/46)) ([799ef33](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/799ef33f5fb2dcc727f97af22b68be37ae77bc62))


### 🐛 Bug Fixes

* correctly link speed sensor to control at first init ([15cd2ec](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/15cd2ecc7f76d257c2d58e2426be95cadb06e22b))

## [1.1.0](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v1.0.3...v1.1.0) (2025-11-24)


### Features

* update FanControl plugin to use IPlugin3 interface ([#12](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/12)) ([d9417a0](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/d9417a0e183caa7f1d6535ec84c96268891fecad))


### Bug Fixes

* named pipe connection timeout error ([#32](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/32)) ([6b3c0a2](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/6b3c0a218e6cded0930e3cad6e66d54aa3be8ae5))

## [1.0.3](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v1.0.2...v1.0.3) (2025-10-23)


### Bug Fixes

* startup ([#8](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/issues/8)) ([0a7c702](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/0a7c702fd0a45b8d087d1233ae21a5a3fa5bf9b5))

## [1.0.2](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/compare/v1.0.1...v1.0.2) (2025-09-11)


### Bug Fixes

* increase named pipe connection timeout ([9dbbbed](https://github.com/antoine-bouteiller/FanControl.LiquidCtl/commit/9dbbbed725390f6a9c331d56b8e20e5561ec3e82))

## [1.0.1](https://github.com/AntoBouteiller/FanControl.LiquidCtl/compare/v1.0.0...v1.0.1) (2025-07-25)


### Bug Fixes

* update readme and fix logs being in french ([0f7cc7a](https://github.com/AntoBouteiller/FanControl.LiquidCtl/commit/0f7cc7abc1c937bd2b452de8cd7378302c83c3f8))

## 1.0.0 (2025-07-22)

### Features

- create project ([182d499](https://github.com/AntoBouteiller/FanControl.LiquidCtl/commit/182d49995d76381da7c7350a5636641e8694cf61))
- optimize performances and minor fixes ([e078ad3](https://github.com/AntoBouteiller/FanControl.LiquidCtl/commit/e078ad37c1f22b44c353dfd2d599fb9411a84e6a))
