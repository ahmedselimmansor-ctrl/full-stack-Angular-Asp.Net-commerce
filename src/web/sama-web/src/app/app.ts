import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationService } from './core/notification.service';
import { LocaleService } from './core/i18n';
import { ThemeService } from './core/theme.service';
import { NoticeStack } from './shared/notice-stack';
import { SiteHeader } from './shared/site-header';
import { SiteFooter } from './shared/site-footer';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NoticeStack, SiteHeader, SiteFooter],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly notifications = inject(NotificationService);

  // Injected purely so both services construct at startup. Each writes to the
  // document element in a constructor effect, and without a reference here
  // nothing would pull them in until some component happened to need one.
  private readonly locale = inject(LocaleService);
  private readonly theme = inject(ThemeService);
}
