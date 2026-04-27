# FMODWrapper — документация

## Содержание

1. [Установка](#1-установка)
2. [FMODWrapper — центральный менеджер](#2-fmodwrapper--центральный-менеджер)
3. [AudioRegistry — реестр путей](#3-audioregistry--реестр-путей)
4. [Банки](#4-банки)
5. [PlayOneShot — короткие SFX](#5-playoneshot--короткие-sfx)
6. [PlayBuilder — управляемое воспроизведение](#6-playbuilder--управляемое-воспроизведение)
7. [FMODEventHandle — управление экземпляром](#7-fmodeventhandle--управление-экземпляром)
8. [FMODAudioSource — пространственный источник](#8-fmodaudiosource--пространственный-источник)
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

1. Добавьте на камеру игрока компонент `StudioListener` из пакета FMOD.
2. Создайте пустой `GameObject`, добавьте `MusicPlayer` если нужна музыка с кроссфейдом.
3. Вызовите `FMODWrapper.Initialize()` из вашего точки входа (bootstrapper / стартовая сцена).

### asmdef

```json
{
  "name": "FMODWrapper",
  "references": ["FMODUnity", "UniTask"]
}
```

---

## 2. FMODWrapper — центральный менеджер

**Статический класс.** Не требует GameObject и не является MonoBehaviour. Все методы вызываются напрямую без `Instance`.

```csharp
// Инициализация — вызвать один раз при старте приложения
FMODWrapper.Initialize();

// Вызывать каждый кадр для очистки завершившихся Handle
FMODWrapper.UpdateHandles();

// Вызвать при завершении приложения
FMODWrapper.Destroy();
```

Типичная интеграция через собственный bootstrapper:

```csharp
public class AudioBootstrapper : MonoBehaviour
{
    private void Awake()  => FMODWrapper.Initialize();
    private void Update() => FMODWrapper.UpdateHandles();
    private void OnDestroy() => FMODWrapper.Destroy();
}
```

Core-банки (`Master`, `Master.strings`) загружаются автоматически внутри `Initialize()`. Список захардкожен в `FMODWrapper.cs` — при необходимости дополните его там.

---

## 3. AudioRegistry — реестр путей

Все пути событий, параметров и банков хранятся как константы в статических классах. Никогда не пишите строки вручную в коде — добавляйте константы в `AudioRegistry.cs`.

```csharp
// Банки (активные)
Banks.Master           // "Master"
Banks.MasterStrings    // "Master.strings"

// Шины (активные)
Buses.Master           // "bus:/"

// Приоритеты голосов
Config.Priority.Highest  // 0
Config.Priority.High     // 1
Config.Priority.Normal   // 2
Config.Priority.Low      // 3

// Пороги дистанции для SampleDataCuller
Config.Distance.UnloadThreshold  // 50f
Config.Distance.LoadThreshold    // 30f
Config.Distance.CheckInterval    // 0.5f
```

Остальные классы (`Events`, `Params`, `Vcas`) заготовлены и закомментированы — раскомментируйте нужные константы или добавьте свои по мере настройки FMOD-проекта.

---

## 4. Банки

### Core-банки

Загружаются автоматически при вызове `FMODWrapper.Initialize()`. Список задаётся в `FMODWrapper.cs`:

```csharp
private static readonly List<string> CoreBanks = new()
{
    Banks.Master,
    Banks.MasterStrings,
    // Banks.Core  ← добавьте свои
};
```

Core-банки никогда не выгружаются.

### Зональные банки — ручная загрузка

```csharp
// Асинхронная загрузка (не блокирует поток)
FMODWrapper.LoadBankAsync("Forest_North");

// С загрузкой Sample Data сразу
FMODWrapper.LoadBankAsync("Forest_North", loadSamples: true);

// С коллбэком по завершении
FMODWrapper.LoadBankAsync("Forest_North", onLoaded: () =>
{
    // банк готов к использованию
});

// Выгрузка
FMODWrapper.UnloadBank("Forest_North");

// Выгрузить все зональные банки сразу
FMODWrapper.UnloadZoneBanks();

// Проверить, загружен ли банк
bool loaded = FMODWrapper.IsBankLoaded("Forest_North");
```

### Sample Data отдельно

```csharp
// Загрузить аудиофайлы уже загруженного банка в RAM
FMODWrapper.LoadSamples("Forest_North");

// Выгрузить аудиофайлы, оставив метаданные
FMODWrapper.UnloadSamples("Forest_North");
```

### Зональные банки — автоматически через триггер

Смотри раздел [BankZoneTrigger](#10-bankzonetrigger--зональная-загрузка).

---

## 5. PlayOneShot — короткие SFX

Самый эффективный способ воспроизведения. Не создаёт `FMODEventHandle`, FMOD освобождает память автоматически. Используйте для всего, чем не нужно управлять после запуска.

```csharp
// 2D
FMODWrapper.PlayOneShot(eventRef);

// 3D — в мировой позиции
FMODWrapper.PlayOneShot(eventRef, transform.position);

// 3D с параметрами
FMODWrapper.PlayOneShot(eventRef, transform.position, new Dictionary<string, float>
{
    { "Surface", 2f },
    { "Size",    1f }
});
```

> `EventReference` задаётся через `[SerializeField] private EventReference _myEvent;` в Inspector.

---

## 6. PlayBuilder — управляемое воспроизведение

Fluent API для запуска события с настройками. Возвращает `FMODEventHandle` для дальнейшего управления.

```csharp
// Минимальный вызов
FMODEventHandle handle = FMODWrapper.Play(eventRef).Start();

// Со всеми опциями
FMODEventHandle handle = FMODWrapper.Play(eventRef)
    .WithParam("WeaponType", 2f)
    .WithParam("Size", 1f)
    .WithVolume(0.8f)
    .WithPriority(Config.Priority.High)
    .AtPosition(transform.position)
    .Start();

// Привязать к GameObject (позиция обновляется каждый кадр)
FMODEventHandle handle = FMODWrapper.Play(eventRef)
    .AttachedTo(gameObject, GetComponent<Rigidbody>())
    .Start();

// Fire & forget — Handle не нужен
FMODWrapper.Play(eventRef)
    .AtPosition(hitPoint)
    .StartAndForget();
```

---

## 7. FMODEventHandle — управление экземпляром

Обёртка над нативным `EventInstance`. Возвращается из `PlayBuilder.Start()` и `FMODWrapper.CreateHandle()`.

### Жизненный цикл

```csharp
// Создать без запуска
FMODEventHandle handle = FMODWrapper.CreateHandle(eventRef);

// Запустить
handle.Play();

// Остановить (с фейдаутом FMOD)
handle.Stop();

// Остановить немедленно и освободить память
handle.Stop(allowFadeout: false, release: true);

// Освободить вручную
handle.Release();

// Или через using (fire & forget)
using var handle = FMODWrapper.Play(eventRef).Start();
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
handle.SetParam("MusicIntensity", 0.8f);

// По ID (быстрее — нет поиска строки)
handle.SetParam(myParameterId, 0.8f);

// Прочитать значение
float? value = handle.GetParam("MusicIntensity");
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
handle.SetTimeline(2000);  // перемотка на 2000 мс
handle.KeyOff();            // отпустить Sustain Point

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
    if (type == EVENT_CALLBACK_TYPE.STOPPED)
        OnSoundFinished();
    return FMOD.RESULT.OK;
}, EVENT_CALLBACK_TYPE.STOPPED);
```

### Поиск активных Handle

```csharp
// Первый активный Handle по пути события
FMODEventHandle existing = FMODWrapper.GetHandle("event:/Music/Combat");

// Все активные Handle
List<FMODEventHandle> all = FMODWrapper.GetHandles("event:/SFX/Footstep");
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

_fmodAS.Play();
_fmodAS.Stop();
_fmodAS.Stop(allowFadeout: false);

_fmodAS.SetPaused(true);
_fmodAS.TogglePause();

_fmodAS.SetParam("Surface", 1f);
_fmodAS.SetVolume(0.7f);
_fmodAS.SetPriority(Config.Priority.High);
_fmodAS.KeyOff();

bool playing = _fmodAS.IsPlaying;
bool paused  = _fmodAS.IsPaused;
```

---

## 9. MusicPlayer — фоновая музыка

Синглтон-компонент с кроссфейдом. Добавьте на любой `GameObject` в сцене.

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
    ("MusicIntensity", 0f));

// Остановить
MusicPlayer.Instance.Stop();
MusicPlayer.Instance.Stop(allowFadeout: false);

// Пауза
MusicPlayer.Instance.SetPaused(true);

// Параметр на ходу
MusicPlayer.Instance.SetParam("MusicIntensity", 0.8f);

// Адаптивная музыка — переход между секциями
MusicPlayer.Instance.KeyOff();

// Состояние
string path  = MusicPlayer.Instance.CurrentTrackPath;
bool playing = MusicPlayer.Instance.IsPlaying;
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

Добавьте компонент на `GameObject` с пространственным источником звука.

### Поля Inspector

| Поле | Описание |
|---|---|
| Event | Событие FMOD, чьи Sample Data контролируются |
| Unload Threshold | Дистанция выгрузки (м), по умолчанию 50 |
| Load Threshold | Дистанция загрузки (м), должна быть < Unload, по умолчанию 30 |
| Check Interval | Интервал проверки (сек), по умолчанию 0.5 |
| Listener Transform | Transform камеры / слушателя |

> Гистерезис (`Load Threshold` < `Unload Threshold`) предотвращает частые переключения на границе зоны.

---

## 12. Глобальные параметры и шины

### Глобальные параметры FMOD

```csharp
FMODWrapper.SetGlobalParam("DayCycle", 0.75f);

float? value = FMODWrapper.GetGlobalParam("DayCycle");
```

### Громкость

```csharp
// Master volume (единственное активное свойство)
FMODWrapper.MasterVolume = 0.8f;

// Произвольная шина по пути
FMODWrapper.SetBusVolume("bus:/SFX", 0.7f);
FMODWrapper.SetBusPaused("bus:/Music", true);
FMODWrapper.SetBusMuted("bus:/Voice", false);

// VCA
FMODWrapper.SetVcaVolume("vca:/Music", 0.6f);
```

### Остановка всех звуков

```csharp
// Все отслеживаемые Handle + снапшоты
FMODWrapper.StopAll();
FMODWrapper.StopAll(allowFadeout: true);

// Только шина
FMODWrapper.StopAllOnBus("bus:/SFX");
```

---

## 13. Снапшоты

Снапшоты FMOD применяют DSP-эффекты к микшеру (приглушение при паузе, эффект воды и т.д.).

```csharp
FMODWrapper.StartSnapshot("snapshot:/Paused");

FMODWrapper.StopSnapshot("snapshot:/Paused");
FMODWrapper.StopSnapshot("snapshot:/Paused", allowFadeout: false);
```

---

## 14. Типичные паттерны

### Пауза игры

```csharp
void PauseGame()
{
    Time.timeScale = 0f;
    FMODWrapper.StartSnapshot("snapshot:/Paused");
    MusicPlayer.Instance.SetPaused(true);
}

void ResumeGame()
{
    Time.timeScale = 1f;
    FMODWrapper.StopSnapshot("snapshot:/Paused");
    MusicPlayer.Instance.SetPaused(false);
}
```

### Footstep с параметром поверхности

```csharp
[SerializeField] private EventReference _footstepEvent;

void Step(SurfaceType surface)
{
    FMODWrapper.PlayOneShot(_footstepEvent, transform.position,
        new Dictionary<string, float> { { "Surface", (float)surface } });
}
```

### Оружие с управляемым звуком

```csharp
[SerializeField] private EventReference _gunEvent;
private FMODEventHandle _gunHandle;

void StartFiring()
{
    _gunHandle = FMODWrapper.Play(_gunEvent)
        .WithParam("WeaponType", 1f)
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
        ("MusicIntensity", 0f));
}

void OnCombatEscalate()
{
    MusicPlayer.Instance.SetParam("MusicIntensity", 1f);
}

void OnCombatEnd()
{
    MusicPlayer.Instance.Play(eventRef_ambient, crossfade: 3f);
}
```

### Колбэк на окончание события

```csharp
FMODEventHandle handle = FMODWrapper.Play(eventRef).Start();

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
| Короткий SFX (выстрел, шаг, UI) | `FMODWrapper.PlayOneShot` |
| SFX с параметрами, без управления после старта | `PlayBuilder.StartAndForget()` |
| SFX, которым нужно управлять (пауза, параметр) | `PlayBuilder.Start()` → `FMODEventHandle` |
| Постоянный источник на объекте | `FMODAudioSource` |
| Фоновая музыка с кроссфейдом | `MusicPlayer` |
| Загрузка банка при входе в локацию | `BankZoneTrigger` |
| Экономия RAM в открытом мире | `SampleDataCuller` |
| Эффект на микшер (реверб, приглушение) | `FMODWrapper.StartSnapshot` / `StopSnapshot` |
| Управление громкостью шины | `FMODWrapper.MasterVolume`, `SetBusVolume` |
