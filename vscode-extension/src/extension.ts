/**
 * Expert Agents for Healthcare — VS Code Extension
 *
 * Integrates two expert agents into GitHub Copilot Chat:
 *   - @fhir-server-expert      — Microsoft FHIR Server knowledge
 *   - @healthcare-components-expert — Healthcare Shared Components knowledge
 *
 * Architecture:
 *   activate() → reads config → creates AgentClient → registers chat participants
 *   deactivate() → disposes all participants
 */

import * as vscode from 'vscode';
import { AgentClient } from './agentClient';
import {
  createFhirServerParticipantHandler,
  fhirFollowUpQuestions,
} from './fhirServerParticipant';
import {
  createHealthcareComponentsParticipantHandler,
  healthcareFollowUpQuestions,
} from './healthcareComponentsParticipant';

/** Extension channel for logging diagnostics. */
let outputChannel: vscode.OutputChannel;

/** Disposables registered during activation. */
const disposables: vscode.Disposable[] = [];

/**
 * Extension activation entry point.
 *
 * @param context - The extension context for registering disposables
 */
export function activate(context: vscode.ExtensionContext): void {
  outputChannel = vscode.window.createOutputChannel('Expert Agents');
  outputChannel.appendLine('Expert Agents: Activating...');

  // ── 1. Read configuration ────────────────────────────────────────
  const config = vscode.workspace.getConfiguration('expertAgents');
  const backendUrl: string = config.get<string>('backendUrl', 'http://localhost:5000');
  const enableStreaming: boolean = config.get<boolean>('enableStreaming', true);

  outputChannel.appendLine(`  backendUrl: ${backendUrl}`);
  outputChannel.appendLine(`  enableStreaming: ${enableStreaming}`);

  // ── 2. Create shared HTTP client ─────────────────────────────────
  const client = new AgentClient({ baseUrl: backendUrl });

  // ── 3. Register chat participants ────────────────────────────────
  const fhirParticipant = vscode.chat.createChatParticipant(
    'expert-agents.fhir-server',
    createFhirServerParticipantHandler(client)
  );
  fhirParticipant.iconPath = new vscode.ThemeIcon('cloud');
  fhirParticipant.followupProvider = {
    provideFollowups: (_result, _context, _token) =>
      fhirFollowUpQuestions.map(
        (q): vscode.ChatFollowup => ({
          prompt: q,
          label: q,
          participant: 'expert-agents.fhir-server',
        })
      ),
  };
  disposables.push(fhirParticipant);

  const healthcareParticipant = vscode.chat.createChatParticipant(
    'expert-agents.healthcare-components',
    createHealthcareComponentsParticipantHandler(client)
  );
  healthcareParticipant.iconPath = new vscode.ThemeIcon('library');
  healthcareParticipant.followupProvider = {
    provideFollowups: (_result, _context, _token) =>
      healthcareFollowUpQuestions.map(
        (q): vscode.ChatFollowup => ({
          prompt: q,
          label: q,
          participant: 'expert-agents.healthcare-components',
        })
      ),
  };
  disposables.push(healthcareParticipant);

  // ── 4. Register commands ─────────────────────────────────────────
  disposables.push(
    vscode.commands.registerCommand('expert-agents.refreshAgents', async () => {
      outputChannel.appendLine('Refreshing expert agents...');
      try {
        const agents = await client.getAgents();
        vscode.window.showInformationMessage(
          `Found ${agents.length} expert agent(s): ${agents.map((a) => a.name).join(', ')}`
        );
        outputChannel.appendLine(`  Found ${agents.length} agent(s)`);
        for (const agent of agents) {
          outputChannel.appendLine(`    - ${agent.name} (${agent.id})`);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        outputChannel.appendLine(`  Error: ${message}`);
        vscode.window.showWarningMessage(
          `Could not refresh agents: ${message}. Using offline mock mode.`
        );
      }
    })
  );

  disposables.push(
    vscode.commands.registerCommand('expert-agents.openSettings', () => {
      vscode.commands.executeCommand(
        'workbench.action.openSettings',
        '@ext:expert-agents.expert-agents-vscode'
      );
    })
  );

  // ── 5. Watch configuration changes ───────────────────────────────
  disposables.push(
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('expertAgents')) {
        outputChannel.appendLine('Configuration changed — please reload the window for changes to take full effect.');
        vscode.window.showInformationMessage(
          'Expert Agents configuration changed. Reload window to apply?',
          'Reload'
        ).then((choice) => {
          if (choice === 'Reload') {
            vscode.commands.executeCommand('workbench.action.reloadWindow');
          }
        });
      }
    })
  );

  // Register all disposables with the extension context
  for (const d of disposables) {
    context.subscriptions.push(d);
  }

  outputChannel.appendLine('Expert Agents: Activation complete.');
}

/**
 * Extension deactivation — clean up resources.
 */
export function deactivate(): void {
  outputChannel?.appendLine('Expert Agents: Deactivating...');
  for (const d of disposables) {
    try {
      d.dispose();
    } catch {
      // Best-effort cleanup
    }
  }
  disposables.length = 0;
  outputChannel?.appendLine('Expert Agents: Deactivation complete.');
}
