# FMODWrapper — документация

## Содержание

1. [Установка](#1-установка)
2. [FMODWrapper — центральный менеджер](#2-fmodwrapper--центральный-менеджер)
3. [AudioRegistry — реестр путей](#3-audioregistry--реестр-путей)
4. [Банки](#4-банки)
5. [PlayOneShot — короткие SFX](#5-playoneshot--короткие-sfx)
6. [PlayBuilder — управляемое воспроизведение](#6-playbuilder--управляемое-воспроизведение)
7. [FMODEventHandle — управление экземпляром](#7-FMODEventHandle--управление-экземпляром)
8. [FMODAudioSource — пространственный источник](#8-FMODAudioSource--пространственный-источник)
9. [MusicPlayer — фоновая музыка](#9-musicplayer--фоновая-музыка)
10. [BankZoneTrigger — зональная загрузка](#10-bankzonetrigger--зональная-загрузка)
11. [SampleDataCuller — выгрузка по дистанции](#11-sampledataculler--выгрузка-по-дистанции)
12. [Глобальные параметры и шины](#12-глобальные-параметры-и-шины)
13. [Снапшоты](#13-снапшоты)
14. [Типичные паттерны](#14-типичные-паттерны)
15. [Что использовать в каких случаях](#15-что-использовать-в-каких-случаях)

---

## 1. Установка

### Зависимости

- [FMOD for Unity](https://www.fmod.com/unity)
- [UniTask](https://github.com/Cysharp/UniTask)

### Настройка сцены

1. Создайте пустой `GameObject` в первой сцене, назовите его `AudioManager`.
2. Добавьте компонент `FMODWrapper`.
3. В поле **Core Banks** укажите банки, которые должны быть в памяти всегда (по умолчанию: `Master`, `Master.strings`, `Core`).
4. Настройте начальные значения громкости шин в Inspector.
5. Добавьте на камеру игрока компонент `StudioListener` из пакета FMOD.

### asmdef

Убедитесь, что в `FMODWrapper.asmdef` присутствуют обе ссылки:

```json
{
  "name": "FMODWrapper",
  "references": ["FMODUnity", "UniTask"]
}
```

---

## 2. FMODWrapper — центральный менеджер

Синглтон. Все обращения к FMOD проходят через него.

```csharp
FMODWrapper.Instance.PlayOneShot(eventRef);
```

Объект помечен `DontDestroyOnLoad` — создайте его один раз в стартовой сцене.

---

## 3. AudioRegistry — реестр путей

Все пути событий, параметров и банков хранятся как константы в статических классах. Никогда не пишите строки вручную в коде.

```csharp
// События
Events.Sfx.Explosion       // "event:/SFX/Explosion"
Events.Music.Combat        // "event:/Music/Combat"
Events.Snapshots.Paused    // "snapshot:/Paused"

// Параметры
Params.MusicIntensity      // "MusicIntensity"
Params.FootstepSurface     // "Surface"

// Банки
Banks.Forest_North         // "Forest_North"
Banks.Core                 // "Core"

// Шины и VCA
Buses.Sfx                  // "bus:/SFX"
VCAs.Music                 // "vca:/Music"

// Приоритеты голосов
Config.Priority.Normal     // 2
Config.Priority.High       // 1
```

Чтобы добавить новое событие — добавьте константу в нужный класс `AudioRegistry.cs`.

---

## 4. Банки

### Core-банки

Загружаются автоматически при старте через список **Core Banks** в Inspector `FMODWrapper`. Никогда не выгружаются.

### Зональные банки — ручная загрузка

```csharp
// Асинхронная загрузка (не блокирует поток)
FMODWrapper.Instance.LoadBankAsync(Banks.Forest_North);

// С загрузкой Sample Data сразу
FMODWrapper.Instance.LoadBankAsync(Banks.Forest_North, loadSamples: true);

// С коллбэком по завершении
FMODWrapper.Instance.LoadBankAsync(Banks.Forest_North, onLoaded: () =>
{
    // банк готов к использованию
});

// Выгрузка
FMODWrapper.Instance.UnloadBank(Banks.Forest_North);

// Выгрузить все зональные банки сразу
FMODWrapper.Instance.UnloadZoneBanks();

// Проверить, загружен ли банк
bool loaded = FMODWrapper.Instance.IsBankLoaded(Banks.Forest_North);
```

### Sample Data отдельно

```csharp
// Загрузить аудиофайлы уже загруженного банка в RAM
FMODWrapper.Instance.LoadSamples(Banks.Forest_North);

// Выгрузить аудиофайлы, оставив метаданные
FMODWrapper.Instance.UnloadSamples(Banks.Forest_North);
```

### Зональные банки — автоматически через триггер

Смотри раздел [BankZoneTrigger](#10-bankzonetrigger--зональная-загрузка).

---

## 5. PlayOneShot — короткие Sfx

Самый эффективный способ воспроизведения. Не создаёт `FMODEventHandle`, FMOD освобождает память автоматически. Используйте для всего, чем не нужно управлять после запуска.

```csharp
// 2D
FMODWrapper.Instance.PlayOneShot(eventRef);

// 3D — в мировой позиции
FMODWrapper.Instance.PlayOneShot(eventRef, transform.position);

// 3D с параметрами
FMODWrapper.Instance.PlayOneShot(eventRef, transform.position, new Dictionary<string, float>
{
    { Params.FootstepSurface, 2f },
    { Params.ExplosionSize,   1f }
});
```

> `EventReference` задаётся через `[SerializeField] private EventReference _myEvent;` в Inspector.

---

## 6. PlayBuilder — управляемое воспроизведение

Fluent API для запуска события с настройками. Возвращает `FMODEventHandle` для дальнейшего управления.

```csharp
// Минимальный вызов
FMODEventHandle handle = FMODWrapper.Instance
    .Play(eventRef)
    .Start();

// Со всеми опциями
FMODEventHandle handle = FMODWrapper.Instance
    .Play(eventRef)
    .WithParam(Params.WeaponType, 2f)
    .WithParam(Params.ExplosionSize, 1f)
    .WithVolume(0.8f)
    .WithPriority(Config.Priority.High)
    .AtPosition(transform.position)
    .Start();

// Привязать к GameObject (позиция обновляется каждый кадр)
FMODEventHandle handle = FMODWrapper.Instance
    .Play(eventRef)
    .AttachedTo(gameObject, GetComponent<Rigidbody>())
    .Start();

// Fire & forget — FMODEventHandle не нужен
FMODWrapper.Instance
    .Play(eventRef)
    .AtPosition(hitPoint)
    .StartAndForget();
```

---

## 7. FMODEventHandle — управление экземпляром

`FMODEventHandle` — обёртка над нативным `EventInstance`. Возвращается из `PlayBuilder.Start()` и `FMODWrapper.CreateHandle()`.

### Жизненный цикл

```csharp
// Создать без запуска
FMODEventHandle handle = FMODWrapper.Instance.CreateHandle(eventRef);

// Запустить
handle.Play();

// Остановить (с фейдаутом FMOD)
handle.Stop();

// Остановить немедленно и освободить память
handle.Stop(allowFadeout: false, release: true);

// Освободить вручную
handle.Release();

// Или через using
using var handle = FMODWrapper.Instance.Play(eventRef).Start();
```

### Пауза

```csharp
handle.SetPaused(true);
handle.SetPaused(false);
handle.TogglePause();
```

### Параметры

```csharp
// По имени
handle.SetParam(Params.MusicIntensity, 0.8f);

// По ID (быстрее — нет поиска строки)
handle.SetParam(myParameterId, 0.8f);

// Прочитать значение
float? value = handle.GetParam(Params.MusicIntensity);
```

### Позиция и пространство

```csharp
// Фиксированная точка
handle.SetPosition(transform.position);

// Привязка к объекту
handle.AttachTo(gameObject, rigidbody);

// Полные атрибуты вручную
handle.SetSpatial(position, velocity, forward, up);
```

### Прочее

```csharp
handle.SetVolume(0.5f);
handle.SetPitch(1.2f);
handle.SetReverb(0, 0.6f);
handle.SetTimeline(2000);   // перемотка на 2000 мс
handle.KeyOff();             // отпустить Sustain Point

int ms = handle.GetTimeline();
```

### Состояние

```csharp
bool valid    = handle.IsValid;
bool playing  = handle.IsPlaying;
bool paused   = handle.IsPaused;
bool stopping = handle.IsStopping;
bool stopped  = handle.IsStopped;

PLAYBACK_STATE state = handle.PlaybackState;
```

### Колбэки

```csharp
handle.SetCallback((type, eventInstance, parameters) =>
{
    if (type == EVENT_CALLBACK_TYPE.SOUND_STOPPED)
        OnSoundFinished();
    return FMOD.RESULT.OK;
}, EVENT_CALLBACK_TYPE.SOUND_STOPPED);
```

### Поиск активных FMODEventHandle

```csharp
// Найти первый активный FMODEventHandle по пути события
FMODEventHandle existing = FMODWrapper.Instance.GetHandle(Events.Music.Combat);

// Все активные FMODEventHandle
List<FMODEventHandle> all = FMODWrapper.Instance.GetHandles(Events.Sfx.Footstep);
```

---

## 8. FMODAudioSource — пространственный источник

Компонент для 3D-источника звука, привязанного к `GameObject`. Аналог `AudioSource`, но для FMOD.

### Настройка в Inspector

| Поле | Описание |
|---|---|
| Event | Событие FMOD Studio |
| Play On Awake | Запустить автоматически при старте |
| Volume | Начальная громкость [0..1] |
| Use 3D | Обновлять позицию автоматически |
| Priority | Приоритет голоса (0 = Highest, 3 = Low) |

### Использование из кода

```csharp
[SerializeField] private FMODAudioSource _fmodAS;

// Воспроизведение
_fmodAS.Play();
_fmodAS.Stop();
_fmodAS.Stop(allowFadeout: false);

// Пауза
_fmodAS.SetPaused(true);
_fmodAS.TogglePause();

// Параметры и громкость
_fmodAS.SetParam(Params.FootstepSurface, 1f);
_fmodAS.SetVolume(0.7f);
_fmodAS.SetPriority(Config.Priority.High);
_fmodAS.KeyOff();

// Состояние
bool playing = _fmodAS.IsPlaying;
bool paused  = _fmodAS.IsPaused;
```

---

## 9. MusicPlayer — фоновая музыка

Синглтон-компонент с кроссфейдом. Добавьте на тот же GameObject, что и `FMODWrapper`.

### Настройка в Inspector

| Поле | Описание |
|---|---|
| Default Crossfade | Длительность кроссфейда по умолчанию (сек) |

### Использование

```csharp
// Сменить трек с кроссфейдом по умолчанию
MusicPlayer.Instance.Play(eventRef);

// Мгновенное переключение
MusicPlayer.Instance.Play(eventRef, crossfade: 0f);

// Кастомный кроссфейд
MusicPlayer.Instance.Play(eventRef, crossfade: 2.5f);

// С начальными параметрами
MusicPlayer.Instance.Play(eventRef, crossfade: 1f,
    (Params.MusicIntensity, 0f));

// Остановить
MusicPlayer.Instance.Stop();
MusicPlayer.Instance.Stop(allowFadeout: false);

// Пауза
MusicPlayer.Instance.SetPaused(true);

// Параметр на ходу (например, нарастание интенсивности)
MusicPlayer.Instance.SetParam(Params.MusicIntensity, 0.8f);

// Адаптивная музыка — переход между секциями
MusicPlayer.Instance.KeyOff();

// Состояние
string path   = MusicPlayer.Instance.CurrentTrackPath;
bool playing  = MusicPlayer.Instance.IsPlaying;
```

> Кроссфейд использует `Time.unscaledDeltaTime` — продолжает работать при `Time.timeScale = 0`.

---

## 10. BankZoneTrigger — зональная загрузка

Компонент автоматической загрузки и выгрузки банков при входе/выходе из области.

### Настройка

1. Создайте пустой `GameObject`.
2. Добавьте `Collider`, включите **Is Trigger**.
3. Добавьте компонент `BankZoneTrigger`.
4. Заполните поля в Inspector.

### Поля Inspector

| Поле | Описание |
|---|---|
| Banks To Load | Имена банков для загрузки (без `.bank`) |
| Load Samples On Enter | Загрузить Sample Data сразу при входе |
| Unload Delay | Задержка выгрузки при выходе (сек) |
| Player Layer | Layer объекта игрока |

### Поведение

- **Вход в зону** → асинхронная загрузка банков.
- **Выход из зоны** → выгрузка с задержкой `Unload Delay`. Если игрок вернулся до истечения задержки — выгрузка отменяется.
- Поддерживает несколько игроков одновременно в зоне (счётчик).

---

## 11. SampleDataCuller — выгрузка по дистанции

Автоматически выгружает аудиофайлы (Sample Data) из RAM, когда источник далеко от слушателя, и загружает обратно при приближении.

### Настройка

Добавьте компонент на `GameObject` рядом с пространственным источником звука.

### Поля Inspector

| Поле | Описание |
|---|---|
| Event | Событие FMOD, чьи Sample Data контролируются |
| Unload Threshold | Дистанция выгрузки (м) |
| Load Threshold | Дистанция загрузки (м, должна быть < Unload) |
| Check Interval | Интервал проверки (сек) |
| Listener Transform | Transform камеры / слушателя |

> Гистерезис (`Load Threshold` < `Unload Threshold`) предотвращает частые переключения на границе зоны.

---

## 12. Глобальные параметры и шины

### Глобальные параметры FMOD

```csharp
// Установить
FMODWrapper.Instance.SetGlobalParam(Params.DayCycle, 0.75f);

// Прочитать
float? value = FMODWrapper.Instance.GetGlobalParam(Params.DayCycle);
```

### Громкость шин

```csharp
// Через свойства (основные шины)
FMODWrapper.Instance.MasterVolume = 0.8f;
FMODWrapper.Instance.MusicVolume  = 0.5f;
FMODWrapper.Instance.SfxVolume    = 1.0f;
FMODWrapper.Instance.VoiceVolume  = 1.0f;

// Произвольная шина по пути
FMODWrapper.Instance.SetBusVolume(Buses.Sfx, 0.7f);
FMODWrapper.Instance.SetBusPaused(Buses.Music, true);
FMODWrapper.Instance.SetBusMuted(Buses.Voice, false);

// VCA
FMODWrapper.Instance.SetVCAVolume(VCAs.Music, 0.6f);
```

### Остановка всех звуков

```csharp
// Все отслеживаемые FMODEventHandle + снапшоты
FMODWrapper.Instance.StopAll();
FMODWrapper.Instance.StopAll(allowFadeout: true);

// Только шина
FMODWrapper.Instance.StopAllOnBus(Buses.Sfx);
```

---

## 13. Снапшоты

Снапшоты FMOD применяют DSP-эффекты к микшеру (например, приглушение при паузе или эффект воды).

```csharp
// Активировать
FMODWrapper.Instance.StartSnapshot(Events.Snapshots.Paused);

// Деактивировать
FMODWrapper.Instance.StopSnapshot(Events.Snapshots.Paused);
FMODWrapper.Instance.StopSnapshot(Events.Snapshots.Paused, allowFadeout: false);
```

---

## 14. Типичные паттерны

### Пауза игры

```csharp
void PauseGame()
{
    Time.timeScale = 0f;
    FMODWrapper.Instance.StartSnapshot(Events.Snapshots.Paused);
    MusicPlayer.Instance.SetPaused(true);
}

void ResumeGame()
{
    Time.timeScale = 1f;
    FMODWrapper.Instance.StopSnapshot(Events.Snapshots.Paused);
    MusicPlayer.Instance.SetPaused(false);
}
```

### Footstep с параметром поверхности

```csharp
[SerializeField] private EventReference _footstepEvent;

void Step(SurfaceType surface)
{
    FMODWrapper.Instance.PlayOneShot(_footstepEvent, transform.position,
        new Dictionary<string, float> { { Params.FootstepSurface, (float)surface } });
}
```

### Оружие с управляемым звуком

```csharp
[SerializeField] private EventReference _gunEvent;
private FMODEventHandle _gunHandle;

void StartFiring()
{
    _gunHandle = FMODWrapper.Instance
        .Play(_gunEvent)
        .WithParam(Params.WeaponType, 1f)
        .WithPriority(Config.Priority.High)
        .AttachedTo(gameObject, _rigidbody)
        .Start();
}

void StopFiring()
{
    _gunHandle?.Stop(allowFadeout: true, release: true);
    _gunHandle = null;
}
```

### Смена музыки при входе в бой

```csharp
void OnCombatStart()
{
    MusicPlayer.Instance.Play(eventRef_combat, crossfade: 1.5f,
        (Params.MusicIntensity, 0f));
}

void OnCombatEscalate()
{
    MusicPlayer.Instance.SetParam(Params.MusicIntensity, 1f);
}

void OnCombatEnd()
{
    MusicPlayer.Instance.Play(eventRef_ambient, crossfade: 3f);
}
```

### Колбэк на окончание события

```csharp
FMODEventHandle handle = FMODWrapper.Instance.Play(eventRef).Start();

handle.SetCallback((type, inst, param) =>
{
    if (type == EVENT_CALLBACK_TYPE.STOPPED)
        OnEffectEnded();
    return FMOD.RESULT.OK;
}, EVENT_CALLBACK_TYPE.STOPPED);
```

---

## 15. Что использовать в каких случаях

| Ситуация | Решение |
|---|---|
| Короткий Sfx (выстрел, шаг, UI) | `PlayOneShot` |
| Sfx с параметрами, без управления после старта | `PlayBuilder.StartAndForget()` |
| Sfx, которым нужно управлять (пауза, параметр) | `PlayBuilder.Start()` → `FMODEventHandle` |
| Постоянный источник на объекте | `FMODAudioSource` |
| Фоновая музыка с кроссфейдом | `MusicPlayer` |
| Загрузка банка при входе в локацию | `BankZoneTrigger` |
| Экономия RAM в открытом мире | `SampleDataCuller` |
| Эффект на микшер (реверб, приглушение) | `StartSnapshot` / `StopSnapshot` |
| Настройка громкости категорий | `MusicVolume`, `SfxVolume`, `SetBusVolume` |
