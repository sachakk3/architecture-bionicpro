import React, { useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';

interface DailyUsage {
  reportDate: string;
  serialNumber: string;
  prosthesisModel: string;
  side: string;
  eventsTotal: number;
  movementsCount: number;
  errorsCount: number;
  activeMinutes: number;
  avgBatteryLevel: number;
  minBatteryLevel: number;
  avgResponseMs: number;
  p95ResponseMs: number;
  avgGripForceN: number;
}

interface UsageReport {
  username: string;
  from: string;
  to: string;
  dataUpdatedAt: string | null;
  totals: {
    daysWithActivity: number;
    eventsTotal: number;
    movementsCount: number;
    errorsCount: number;
    activeMinutes: number;
  };
  days: DailyUsage[];
}

const errorMessages: Record<number, string> = {
  401: 'Session expired, please log in again',
  403: 'You do not have access to reports',
  503: 'Report storage is temporarily unavailable, try again later',
};

const ReportPage: React.FC = () => {
  const { keycloak, initialized } = useKeycloak();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<UsageReport | null>(null);

  const downloadReport = async () => {
    if (!keycloak?.token) {
      setError('Not authenticated');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      // Tokens are short-lived: refresh if it expires in less than 30 seconds
      await keycloak.updateToken(30);

      const response = await fetch(`${process.env.REACT_APP_API_URL}/reports`, {
        headers: {
          'Authorization': `Bearer ${keycloak.token}`
        }
      });

      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? errorMessages[response.status] ?? `Request failed: ${response.status}`);
      }

      setReport(await response.json());
    } catch (err) {
      setReport(null);
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  const saveReport = () => {
    if (!report) return;
    const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `report-${report.username}-${report.from}-${report.to}.json`;
    link.click();
    URL.revokeObjectURL(url);
  };

  if (!initialized) {
    return <div>Loading...</div>;
  }

  if (!keycloak.authenticated) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
        <button
          onClick={() => keycloak.login()}
          className="px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600"
        >
          Login
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
      <div className="p-8 bg-white rounded-lg shadow-md">
        <h1 className="text-2xl font-bold mb-6">Usage Reports</h1>

        <button
          onClick={downloadReport}
          disabled={loading}
          className={`px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 ${
            loading ? 'opacity-50 cursor-not-allowed' : ''
          }`}
        >
          {loading ? 'Generating Report...' : 'Download Report'}
        </button>

        {error && (
          <div className="mt-4 p-4 bg-red-100 text-red-700 rounded">
            {error}
          </div>
        )}

        {report && (
          <div className="mt-6">
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-lg font-semibold">
                {report.username}: {report.from} — {report.to}
              </h2>
              <button
                onClick={saveReport}
                className="ml-4 px-3 py-1 text-sm border border-blue-500 text-blue-600 rounded hover:bg-blue-50"
              >
                Save JSON
              </button>
            </div>
            <p className="text-sm text-gray-600 mb-4">
              Active days: {report.totals.daysWithActivity} · Movements: {report.totals.movementsCount} · Errors:{' '}
              {report.totals.errorsCount} · Active minutes: {report.totals.activeMinutes}
              {report.dataUpdatedAt && <> · Updated: {new Date(report.dataUpdatedAt).toLocaleString()}</>}
            </p>

            {report.days.length === 0 ? (
              <p className="text-gray-500">No data for this period</p>
            ) : (
              <div className="max-h-96 overflow-auto">
                <table className="text-sm border-collapse">
                  <thead className="sticky top-0 bg-gray-50">
                    <tr className="text-left">
                      <th className="px-2 py-1">Date</th>
                      <th className="px-2 py-1">Prosthesis</th>
                      <th className="px-2 py-1 text-right">Movements</th>
                      <th className="px-2 py-1 text-right">Errors</th>
                      <th className="px-2 py-1 text-right">Active min</th>
                      <th className="px-2 py-1 text-right">Battery avg / min, %</th>
                      <th className="px-2 py-1 text-right">Response avg / p95, ms</th>
                      <th className="px-2 py-1 text-right">Grip avg, N</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.days.map((d) => (
                      <tr key={`${d.reportDate}-${d.serialNumber}`} className="border-t">
                        <td className="px-2 py-1 whitespace-nowrap">{d.reportDate}</td>
                        <td className="px-2 py-1 whitespace-nowrap">
                          {d.prosthesisModel} ({d.serialNumber}, {d.side})
                        </td>
                        <td className="px-2 py-1 text-right">{d.movementsCount}</td>
                        <td className="px-2 py-1 text-right">{d.errorsCount}</td>
                        <td className="px-2 py-1 text-right">{d.activeMinutes}</td>
                        <td className="px-2 py-1 text-right">
                          {d.avgBatteryLevel.toFixed(0)} / {d.minBatteryLevel}
                        </td>
                        <td className="px-2 py-1 text-right">
                          {d.avgResponseMs.toFixed(0)} / {d.p95ResponseMs.toFixed(0)}
                        </td>
                        <td className="px-2 py-1 text-right">{d.avgGripForceN.toFixed(1)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default ReportPage;
