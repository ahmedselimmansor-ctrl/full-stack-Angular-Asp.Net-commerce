import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../core/admin.service';
import { LocaleService, TranslatePipe } from '../../core/i18n';
import { AuthService } from '../../core/auth.service';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import type { Paged, UserSummary } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { type ChipSpec, StatusChip } from '../../shared/ui/status-chip';

/** The roles the API recognises. Kept in step with SamaRoles on the server. */
const ROLES = ['customer', 'support', 'catalog_manager', 'order_manager', 'administrator'] as const;

@Component({
  selector: 'app-admin-users',
  imports: [
    FormsModule, DatePipe, PageHeader, Skeleton, EmptyState, StatusChip, ConfirmDialog,
    HasPermissionDirective,
  TranslatePipe,
  ],
  template: `
    <app-page-header
      title="Users"
      description="Accounts on this storefront. Roles are bundles of permissions." />

    <div class="filters">
      <label class="field">
        <span>{{ 'common.search' | t }}</span>
        <input type="search" [(ngModel)]="search" (input)="onSearch()" [placeholder]="'admin.users.searchHint' | t" />
      </label>
    </div>

    @if (loading()) {
      <app-skeleton [count]="5" [height]="60" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state [icon]="EmptyIcons.user" [title]="'admin.users.empty' | t" [description]="'admin.users.emptyCopy' | t" />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ 'admin.users.lede' | t }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ 'admin.users.person' | t }}</th>
              <th scope="col">{{ 'admin.users.roles' | t }}</th>
              <th scope="col">{{ 'admin.users.lastSignIn' | t }}</th>
              <th scope="col">State</th>
              <th scope="col"><span class="sr-only">{{ 'common.actions' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (user of page()!.items; track user.id) {
              <tr>
                <td>
                  <span class="name">{{ user.displayName }}</span>
                  <span class="email muted">{{ user.email }}</span>
                </td>
                <td>
                  <div class="roles">
                    @for (role of user.roles; track role) {
                      <button type="button" class="role" (click)="askToRemoveRole(user, role)"
                              [attr.aria-label]="'Remove the ' + label(role) + ' role from ' + user.displayName">
                        {{ label(role) }} <span aria-hidden="true">×</span>
                      </button>
                    }
                    @if (user.roles.length === 0) { <span class="muted">—</span> }
                  </div>
                </td>
                <td>{{ user.lastLoginAt ? (user.lastLoginAt | date: 'mediumDate') : 'Never' }}</td>
                <td>
                  @if (user.isActive) {
                    <app-status-chip [spec]="activeChip" />
                  } @else {
                    <app-status-chip [spec]="deactivatedChip" />
                  }
                </td>
                <td class="actions">
                  <div *samaHasPermission="assignPermission" class="grant">
                    <select [attr.aria-label]="'Grant a role to ' + user.displayName"
                            (change)="grant(user, $event)">
                      <option value="">{{ 'admin.users.grantRole' | t }}</option>
                      @for (role of assignableRoles(user); track role) {
                        <option [value]="role">{{ label(role) }}</option>
                      }
                    </select>
                  </div>

                  @if (user.isActive && user.id !== auth.currentUser()?.id) {
                    <button *samaHasPermission="writePermission" type="button"
                            class="link link--danger" (click)="askToDeactivate(user)">{{ 'admin.users.deactivate' | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="note muted">
        A role change rotates the account's security stamp, which invalidates every token it
        currently holds — so the new permissions apply immediately rather than at the next sign-in.
      </p>
    }

    <app-confirm-dialog
      [open]="pendingAction() !== null"
      [title]="pendingAction()?.title ?? ''"
      [message]="pendingAction()?.message ?? ''"
      [confirmLabel]="pendingAction()?.confirmLabel ?? 'Confirm'"
      [destructive]="true"
      (confirmed)="runPendingAction()"
      (cancelled)="pendingAction.set(null)" />
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .name { font-weight: 600; display: block; }
    .email { font-size: 0.82rem; }
    .roles { display: flex; flex-wrap: wrap; gap: 0.3rem; }
    .role {
      display: inline-flex; align-items: center; gap: 0.3rem;
      padding: 0.15rem 0.5rem; border-radius: 999px;
      border: 1px solid var(--line); background: var(--surface-2);
      font: inherit; font-size: 0.76rem; cursor: pointer; color: var(--text-soft);
    }
    .role:hover { border-color: var(--critical); color: var(--critical); }
    .grant { display: inline-block; }
    .grant select {
      font: inherit; font-size: 0.82rem; padding: 0.25rem 0.4rem;
      border: 1px solid var(--line); border-radius: var(--sama-radius-sm);
      background: var(--surface); color: var(--text);
    }
    .note { font-size: 0.82rem; margin-block-start: 1rem; }
  `,
})
export class AdminUserTable {
  protected readonly locale = inject(LocaleService);
  protected readonly EmptyIcons = EmptyIcons;
  protected readonly activeChip: ChipSpec = { label: 'status.user.active', tone: 'good', translated: true };
  protected readonly deactivatedChip: ChipSpec = { label: 'status.user.deactivated', tone: 'bad', translated: true };

  private readonly admin = inject(AdminService);
  private readonly notifications = inject(NotificationService);
  protected readonly auth = inject(AuthService);

  protected readonly page = signal<Paged<UserSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly assignPermission = ['identity:role:assign'];
  protected readonly writePermission = ['identity:user:write'];

  protected readonly pendingAction = signal<{
    title: string;
    message: string;
    confirmLabel: string;
    run: () => Promise<void>;
  } | null>(null);

  protected search = '';
  private currentPage = 1;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    void this.load();
  }

  protected label(role: string): string {
    return role.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }

  protected assignableRoles(user: UserSummary): readonly string[] {
    return ROLES.filter(role => !user.roles.includes(role));
  }

  protected onSearch(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.currentPage = 1;
      void this.load();
    }, 300);
  }

  protected async grant(user: UserSummary, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const role = select.value;
    select.value = '';

    if (!role) return;

    try {
      await this.admin.assignRole(user.id, role);
      this.notifications.success(
        this.locale.t('notify.roleAssigned', { name: user.displayName, role: this.label(role) }),
      );
      await this.load();
    } catch {
      // Reported by the interceptor.
    }
  }

  protected askToRemoveRole(user: UserSummary, role: string): void {
    this.pendingAction.set({
      title: this.locale.t('admin.users.removeRoleTitle', { role: this.label(role) }),
      message: this.locale.t('admin.users.removeRoleCopy', { name: user.displayName }),
      confirmLabel: this.locale.t('admin.users.removeRole'),
      run: async () => {
        await this.admin.removeRole(user.id, role);
        this.notifications.success(
          this.locale.t('notify.roleRemoved', { role: this.label(role), name: user.displayName }),
        );
      },
    });
  }

  protected askToDeactivate(user: UserSummary): void {
    this.pendingAction.set({
      title: this.locale.t('admin.users.deactivateTitle'),
      message: this.locale.t('admin.users.deactivateCopy', { name: user.displayName }),
      confirmLabel: 'Deactivate',
      run: async () => {
        await this.admin.deactivateUser(user.id);
        this.notifications.success(
          this.locale.t('notify.userDeactivated', { name: user.displayName }),
        );
      },
    });
  }

  protected async runPendingAction(): Promise<void> {
    const action = this.pendingAction();
    this.pendingAction.set(null);
    if (!action) return;

    try {
      await action.run();
      await this.load();
    } catch {
      // The API refuses self-deactivation and removing your own last administrator role;
      // the interceptor surfaces the reason.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(
        await this.admin.users({ page: this.currentPage, search: this.search.trim() || null }),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
