(function () {
    "use strict";

    const BUTTON_ID = "watch-history-manager-startpoint-button";
    const CHECK_INTERVAL_MS = 1000;

    let lastItemId = null;
    let isBusy = false;

    function log(message, data) {
        if (data !== undefined) {
            console.log("[WatchHistoryManager]", message, data);
        } else {
            console.log("[WatchHistoryManager]", message);
        }
    }

    function getLanguages() {
        const languages = [];

        if (document.documentElement && document.documentElement.lang) {
            languages.push(document.documentElement.lang);
        }

        if (navigator.languages && navigator.languages.length) {
            languages.push(...navigator.languages);
        }

        if (navigator.language) {
            languages.push(navigator.language);
        }

        return languages.length ? languages : ["en"];
    }

    function isGermanLanguage() {
        return getLanguages().some(function (language) {
            return String(language).toLowerCase().startsWith("de");
        });
    }

    function translate(key, values) {
        const german = isGermanLanguage();

        const translations = {
            startPointButton: german ? "Startpunkt setzen" : "Set starting point",

            noUser: german ? "Kein Jellyfin-Benutzer gefunden." : "No Jellyfin user found.",

            startPointDisabled: german ? "Startpunkt setzen ist im Plugin deaktiviert." : "Set starting point is disabled in the plugin.",

            notEpisode: german ? "Diese Funktion ist nur für Episoden verfügbar." : "This feature is only available for episodes.",

            noSeries: german ? "Für diese Episode konnte keine Serie gefunden werden." : "No series could be found for this episode.",

            episodeNotFound: german ? "Die aktuelle Folge wurde in der Episodenliste nicht gefunden." : "The current episode could not be found in the episode list.",

            success: german ? "Startpunkt wurde gesetzt." : "Starting point has been set.",

            failed: german ? "Startpunkt konnte nicht gesetzt werden. Details stehen in der Browser-Konsole." : "Could not set starting point. Details are available in the browser console.",

            confirmTitle: german ? "Startpunkt auf diese Folge setzen?" : "Set this episode as the starting point?",

            confirmPrevious: german ? "Alle Folgen davor werden als gesehen markiert." : "All previous episodes will be marked as watched.",

            confirmCurrentAndFollowing: german ? "Diese Folge und alle danach werden als ungesehen markiert." : "This episode and all following episodes will be marked as unwatched.",

            confirmEpisode: german ? "Folge" : "Episode",

            fallbackSeries: german ? "Serie" : "Series",
        };

        let text = translations[key] || key;

        if (values) {
            Object.keys(values).forEach(function (name) {
                text = text.replaceAll("{" + name + "}", values[name]);
            });
        }

        return text;
    }

    function getStartPointConfirmationText(currentItem) {
        return translate("confirmTitle") + "\n\n" + translate("confirmPrevious") + "\n" + translate("confirmCurrentAndFollowing") + "\n\n" + translate("confirmEpisode") + ": " + (currentItem.SeriesName || translate("fallbackSeries")) + " - " + currentItem.Name;
    }

    function getCurrentItemIdFromUrl() {
        const href = window.location.href;
        const match = href.match(/[?&]id=([a-f0-9-]{32,36})/i);

        if (!match) {
            return null;
        }

        return match[1];
    }

    function getCurrentUserId() {
        if (window.ApiClient && typeof ApiClient.getCurrentUserId === "function") {
            return ApiClient.getCurrentUserId();
        }

        return null;
    }

    function getApiUrl(path, query) {
        if (!window.ApiClient || typeof ApiClient.getUrl !== "function") {
            throw new Error("ApiClient.getUrl is not available.");
        }

        return ApiClient.getUrl(path, query || {});
    }

    function apiRequest(method, path, query, body) {
        const request = {
            type: method,
            url: getApiUrl(path, query),
        };

        if (body !== undefined) {
            request.data = JSON.stringify(body);
            request.contentType = "application/json";
        }

        return ApiClient.ajax(request);
    }

    function showMessage(message) {
        if (window.Dashboard && typeof Dashboard.alert === "function") {
            Dashboard.alert(message);
            return;
        }

        alert(message);
    }

    function isFeatureEnabled(features, pascalCaseName, camelCaseName) {
        if (!features) {
            return false;
        }

        if (features[pascalCaseName] !== undefined) {
            return Boolean(features[pascalCaseName]);
        }

        if (features[camelCaseName] !== undefined) {
            return Boolean(features[camelCaseName]);
        }

        return false;
    }

    function getFeatureValue(features, pascalCaseName, camelCaseName, fallback) {
        if (!features) {
            return fallback;
        }

        if (features[pascalCaseName] !== undefined) {
            return features[pascalCaseName];
        }

        if (features[camelCaseName] !== undefined) {
            return features[camelCaseName];
        }

        return fallback;
    }

    async function getFeatures() {
        try {
            return await apiRequest("GET", "WatchHistoryManager/Features");
        } catch (error) {
            log("Could not load feature flags. Button stays hidden.", error);

            return {
                EnableStartPointButton: false,
                IgnoreSpecials: true,
            };
        }
    }

    async function getItem(userId, itemId) {
        if (window.ApiClient && typeof ApiClient.getItem === "function") {
            return ApiClient.getItem(userId, itemId);
        }

        return apiRequest("GET", "Items/" + itemId, { UserId: userId });
    }

    async function getEpisodes(userId, seriesId) {
        const result = await apiRequest("GET", "Shows/" + seriesId + "/Episodes", {
            UserId: userId,
            Fields: "UserData,SortName",
            IsMissing: false,
        });

        return result.Items || result.items || [];
    }

    function getEpisodeSortValue(episode) {
        const season = episode.ParentIndexNumber ?? episode.parentIndexNumber ?? 999999;
        const episodeNumber = episode.IndexNumber ?? episode.indexNumber ?? 999999;
        const name = episode.Name || episode.name || "";

        return {
            season: season,
            episodeNumber: episodeNumber,
            name: name,
        };
    }

    function sortEpisodes(episodes) {
        return episodes.slice().sort(function (a, b) {
            const left = getEpisodeSortValue(a);
            const right = getEpisodeSortValue(b);

            if (left.season !== right.season) {
                return left.season - right.season;
            }

            if (left.episodeNumber !== right.episodeNumber) {
                return left.episodeNumber - right.episodeNumber;
            }

            return left.name.localeCompare(right.name);
        });
    }

    async function markPlayed(itemId) {
        return apiRequest("POST", "UserPlayedItems/" + itemId);
    }

    async function markUnplayed(itemId) {
        return apiRequest("DELETE", "UserPlayedItems/" + itemId);
    }

    async function runLimited(items, concurrency, worker) {
        let index = 0;
        const workers = [];

        async function runWorker() {
            while (index < items.length) {
                const currentIndex = index;
                index += 1;
                await worker(items[currentIndex], currentIndex);
            }
        }

        const workerCount = Math.min(concurrency, items.length);

        for (let i = 0; i < workerCount; i += 1) {
            workers.push(runWorker());
        }

        await Promise.all(workers);
    }

    function isPlayed(episode) {
        const userData = episode.UserData || episode.userData;
        return Boolean(userData && (userData.Played || userData.played));
    }

    function hasProgress(episode) {
        const userData = episode.UserData || episode.userData;
        const positionTicks = userData ? (userData.PlaybackPositionTicks ?? userData.playbackPositionTicks ?? 0) : 0;

        return positionTicks > 0;
    }

    function getItemId(item) {
        return item.Id || item.id;
    }

    function getItemType(item) {
        return item.Type || item.type;
    }

    function getSeriesId(item) {
        return item.SeriesId || item.seriesId;
    }

    function getSeasonNumber(item) {
        return item.ParentIndexNumber ?? item.parentIndexNumber;
    }

    async function setStartPoint(itemId) {
        if (isBusy) {
            return;
        }

        isBusy = true;

        try {
            const userId = getCurrentUserId();

            if (!userId) {
                showMessage(translate("noUser"));
                return;
            }

            const features = await getFeatures();

            if (!isFeatureEnabled(features, "EnableStartPointButton", "enableStartPointButton")) {
                showMessage(translate("startPointDisabled"));
                return;
            }

            const ignoreSpecials = Boolean(getFeatureValue(features, "IgnoreSpecials", "ignoreSpecials", true));

            const currentItem = await getItem(userId, itemId);

            if (!currentItem || getItemType(currentItem) !== "Episode") {
                showMessage(translate("notEpisode"));
                return;
            }

            const seriesId = getSeriesId(currentItem);

            if (!seriesId) {
                showMessage(translate("noSeries"));
                return;
            }

            const confirmation = confirm(getStartPointConfirmationText(currentItem));

            if (!confirmation) {
                return;
            }

            let episodes = await getEpisodes(userId, seriesId);

            if (ignoreSpecials) {
                episodes = episodes.filter(function (episode) {
                    return getSeasonNumber(episode) !== 0;
                });
            }

            const sortedEpisodes = sortEpisodes(episodes);

            const currentIndex = sortedEpisodes.findIndex(function (episode) {
                return getItemId(episode) === getItemId(currentItem);
            });

            if (currentIndex < 0) {
                showMessage(translate("episodeNotFound"));
                return;
            }

            const episodesBefore = sortedEpisodes.slice(0, currentIndex);
            const episodesFromCurrent = sortedEpisodes.slice(currentIndex);

            log("Setting start point", {
                currentItem: currentItem.Name || currentItem.name,
                markPlayedCount: episodesBefore.length,
                markUnplayedCount: episodesFromCurrent.length,
            });

            await runLimited(episodesBefore, 5, async function (episode) {
                if (!isPlayed(episode)) {
                    await markPlayed(getItemId(episode));
                }
            });

            await runLimited(episodesFromCurrent, 5, async function (episode) {
                if (isPlayed(episode) || hasProgress(episode)) {
                    await markUnplayed(getItemId(episode));
                }
            });

            showMessage(translate("success"));
        } catch (error) {
            console.error("[WatchHistoryManager] Failed to set start point.", error);
            showMessage(translate("failed"));
        } finally {
            isBusy = false;
        }
    }

    function removeButton() {
        const existingButton = document.getElementById(BUTTON_ID);

        if (existingButton) {
            existingButton.remove();
        }
    }

    function createButton(itemId) {
        removeButton();

        const button = document.createElement("button");
        button.id = BUTTON_ID;
        button.type = "button";
        button.textContent = translate("startPointButton");

        button.style.position = "fixed";
        button.style.right = "24px";
        button.style.bottom = "96px";
        button.style.zIndex = "99999";
        button.style.padding = "12px 18px";
        button.style.borderRadius = "8px";
        button.style.border = "none";
        button.style.cursor = "pointer";
        button.style.background = "#00a4dc";
        button.style.color = "#fff";
        button.style.fontSize = "14px";
        button.style.fontWeight = "600";
        button.style.boxShadow = "0 4px 12px rgba(0,0,0,0.35)";

        button.addEventListener("click", function () {
            setStartPoint(itemId);
        });

        document.body.appendChild(button);
    }

    async function refreshButton() {
        const itemId = getCurrentItemIdFromUrl();

        if (!itemId) {
            lastItemId = null;
            removeButton();
            return;
        }

        if (itemId === lastItemId && document.getElementById(BUTTON_ID)) {
            return;
        }

        lastItemId = itemId;

        try {
            const features = await getFeatures();

            if (!isFeatureEnabled(features, "EnableStartPointButton", "enableStartPointButton")) {
                removeButton();
                return;
            }

            const userId = getCurrentUserId();

            if (!userId) {
                removeButton();
                return;
            }

            const item = await getItem(userId, itemId);

            if (!item || getItemType(item) !== "Episode") {
                removeButton();
                return;
            }

            createButton(itemId);
        } catch (error) {
            log("Could not refresh start point button.", error);
            removeButton();
        }
    }

    function start() {
        log("Start point button script loaded.");
        setInterval(refreshButton, CHECK_INTERVAL_MS);
    }

    start();
})();
