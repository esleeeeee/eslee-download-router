declare namespace chrome {
  namespace runtime {
    const lastError: { message?: string } | undefined;

    function sendNativeMessage(
      application: string,
      message: unknown,
      responseCallback: (response: unknown) => void,
    ): void;
  }

  namespace downloads {
    interface DownloadItem {
      id: number;
      url: string;
      finalUrl?: string;
      referrer?: string;
      filename: string;
      state: "in_progress" | "interrupted" | "complete";
      error?: string;
      incognito: boolean;
      byExtensionId?: string;
      byExtensionName?: string;
      /** ISO 8601 time the browser started the transfer. Past for history replays. */
      startTime?: string;
      exists?: boolean;
      paused?: boolean;
    }

    interface StringDelta {
      current?: string;
      previous?: string;
    }

    interface StateDelta {
      current?: "in_progress" | "interrupted" | "complete";
      previous?: "in_progress" | "interrupted" | "complete";
    }

    interface DownloadDelta {
      id: number;
      filename?: StringDelta;
      state?: StateDelta;
      error?: StringDelta;
    }

    const onCreated: {
      addListener(callback: (downloadItem: DownloadItem) => void): void;
    };

    const onChanged: {
      addListener(callback: (downloadDelta: DownloadDelta) => void): void;
    };

    const onErased: {
      addListener(callback: (downloadId: number) => void): void;
    };

    function search(query: { id?: number }, callback: (results: DownloadItem[]) => void): void;
  }
}
